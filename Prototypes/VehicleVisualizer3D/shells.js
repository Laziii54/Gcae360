import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';

/**
 * Adding a future real vehicle model (a different brand, a licensed asset, a properly
 * panel-separated rig) means adding ONE entry here — nothing else in the app hardcodes
 * model names. `panelMap`, when a model's file actually ships separate door/hood/trunk
 * nodes, lists {panelId: nodeName} so the same panel-control UI in app-ui.js can drive it;
 * left empty here because neither sourced model below has separable panels (confirmed by
 * inspection — see the comparison report).
 */
export const SHELL_DEFS = {
  quaternius: {
    label: 'Quaternius — Car',
    url: 'models/quaternius-car.glb',
    rotateY: Math.PI / 2,
    license: 'CC0 (საჯარო დომენი — ატრიბუცია საჭირო არაა)',
    source: 'poly.pizza, "Cars Bundle" by Quaternius',
    note: 'ერთი შეჭედილი mesh (მასალების მიხედვით დაყოფილი: სხეული/მინები/შუქები) — ფიზიკური პანელები (კარი/კაპოტი) ცალკე არ არსებობს ფაილში.',
    size: '~165 KB',
    panelMap: null,
  },
  sloyd: {
    label: 'TopoSphere Sedan (Sloyd)',
    url: 'models/sloyd-sedan.glb',
    rotateY: 0,
    license: 'CC BY 4.0 — საჭიროა ავტორის მითითება აპში',
    source: 'sloyd.ai, "A Red Mid-sized Four-door Sedan" by TopoSphere',
    note: 'სრულად ერთიანი mesh, ერთი მასალა — არც პანელები და არც მასალის ჯგუფებია ცალკე.',
    size: '~5.7 MB',
    panelMap: null,
  },
};

export const shellMeta = {
  procedural: {
    label: 'პროცედურული (ახლანდელი)',
    license: '—',
    source: 'ამ პროტოტიპისთვის აგებული საკუთარი გეომეტრია',
    note: 'ყველა პანელი ცალ-ცალკე ობიექტია — დამალვა/გამჭვირვალობა/იზოლაცია მუშაობს თითოეულზე ცალკე.',
    triangles: '~4,200',
    size: '0 KB (კოდში აგებული)',
  },
};
Object.entries(SHELL_DEFS).forEach(([key, def]) => {
  shellMeta[key] = { label: def.label, license: def.license, source: def.source, note: def.note, triangles: 'იტვირთება…', size: def.size };
});

export const shellGroups = {};
Object.keys(SHELL_DEFS).forEach((key) => { shellGroups[key] = new THREE.Group(); shellGroups[key].visible = false; });

const TARGET_LENGTH = 430;
const loader = new GLTFLoader();
const pending = {};

function tagMaterials(root) {
  root.traverse((o) => {
    if (!o.isMesh) return;
    o.castShadow = true;
    o.receiveShadow = true;
    const wasArray = Array.isArray(o.material);
    const mats = (wasArray ? o.material : [o.material]).map((m) => m.clone());
    o.material = wasArray ? mats : mats[0];
    mats.forEach((mat) => {
      mat.userData.baseColor = mat.color ? mat.color.clone() : new THREE.Color(0xffffff);
      mat.userData.baseOpacity = mat.opacity ?? 1;
      mat.userData.baseTransparent = mat.transparent ?? false;
      mat.userData.baseEmissive = mat.emissive ? mat.emissive.clone() : new THREE.Color(0, 0, 0);
      mat.userData.baseEmissiveIntensity = mat.emissiveIntensity ?? 1;
      mat.side = THREE.DoubleSide;
    });
    o.userData.isShellPart = true;
  });
}

function normalizeAndAdd(group, loadedRoot, rotateY) {
  loadedRoot.rotation.y = rotateY;
  loadedRoot.updateMatrixWorld(true);
  const box = new THREE.Box3().setFromObject(loadedRoot);
  const size = new THREE.Vector3();
  box.getSize(size);
  const longest = Math.max(size.x, size.z);
  const scale = longest > 0 ? TARGET_LENGTH / longest : 1;
  loadedRoot.scale.setScalar(scale);
  loadedRoot.updateMatrixWorld(true);

  const box2 = new THREE.Box3().setFromObject(loadedRoot);
  const center = new THREE.Vector3();
  box2.getCenter(center);
  loadedRoot.position.x -= center.x;
  loadedRoot.position.z -= center.z;
  loadedRoot.position.y -= box2.min.y;

  tagMaterials(loadedRoot);
  group.add(loadedRoot);

  let triCount = 0;
  loadedRoot.traverse((o) => {
    if (o.isMesh && o.geometry?.index) triCount += o.geometry.index.count / 3;
    else if (o.isMesh && o.geometry?.attributes?.position) triCount += o.geometry.attributes.position.count / 3;
  });
  const box3 = new THREE.Box3().setFromObject(loadedRoot);
  return { length: size, scale, triangles: Math.round(triCount), height: box3.max.y - box3.min.y, minY: box3.min.y };
}

/** Loads a shell's GLB only the first time it's actually selected — not on page load.
 *  Keeps the default (mobile-bound) payload at 0 extra bytes until a user opts into
 *  the comparison, which matters once this prototype targets a MAUI WebView. */
export function ensureShellLoaded(scene, key) {
  if (pending[key]) return pending[key];
  const def = SHELL_DEFS[key];
  scene.add(shellGroups[key]);
  pending[key] = new Promise((resolve) => {
    loader.load(
      def.url,
      (gltf) => resolve(normalizeAndAdd(shellGroups[key], gltf.scene, def.rotateY)),
      undefined,
      (err) => { console.error(`[shells] failed to load ${key}:`, err); resolve({ error: String(err) }); },
    );
  });
  return pending[key];
}

export function isShellLoaded(key) {
  return !!pending[key];
}
