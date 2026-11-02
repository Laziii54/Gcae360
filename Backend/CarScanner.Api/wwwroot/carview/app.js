import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';

/* ----------------------------------------------------------------------- */
/* Setup                                                                    */
/* ----------------------------------------------------------------------- */

const canvas = document.getElementById('three-canvas');
const stage = document.getElementById('stage');
const loadingEl = document.getElementById('loading');

const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false });
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.05;
renderer.localClippingEnabled = true;

const scene = new THREE.Scene();
scene.background = new THREE.Color(0x101216);
scene.fog = new THREE.Fog(0x101216, 900, 2200);

const camera = new THREE.PerspectiveCamera(42, 1, 1, 5000);
camera.position.set(360, 220, 420);

const controls = new OrbitControls(camera, renderer.domElement);
controls.target.set(0, 60, 0);
controls.enableDamping = true;
controls.dampingFactor = 0.08;
controls.minDistance = 120;
controls.maxDistance = 1400;
controls.maxPolarAngle = Math.PI * 0.98;

const pmrem = new THREE.PMREMGenerator(renderer);
scene.environment = pmrem.fromScene(new RoomEnvironment(), 0.035).texture;

// Lights
scene.add(new THREE.AmbientLight(0x404650, 0.9));
const keyLight = new THREE.DirectionalLight(0xffffff, 2.4);
keyLight.position.set(260, 420, 260);
keyLight.castShadow = true;
keyLight.shadow.mapSize.set(2048, 2048);
keyLight.shadow.camera.near = 50;
keyLight.shadow.camera.far = 1200;
keyLight.shadow.camera.left = -400;
keyLight.shadow.camera.right = 400;
keyLight.shadow.camera.top = 400;
keyLight.shadow.camera.bottom = -400;
keyLight.shadow.bias = -0.0005;
scene.add(keyLight);
const fillLight = new THREE.DirectionalLight(0x6ea7c9, 0.6);
fillLight.position.set(-300, 180, -220);
scene.add(fillLight);
const rimLight = new THREE.DirectionalLight(0x6fb8c9, 0.16);
rimLight.position.set(-200, 120, 320);
scene.add(rimLight);

// A quiet studio floor instead of an asphalt road — the bright dashed lane line and
// speckle noise were competing with the car for attention. This is a soft radial-gradient
// sweep (lighter directly under the car, fading to the background colour at the edges)
// with a faint reference grid, the way automotive configurator/studio renders look.
function makeStudioFloorTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 1024;
  const ctx = c.getContext('2d');
  const grad = ctx.createRadialGradient(512, 512, 40, 512, 512, 512);
  grad.addColorStop(0, '#23262c');
  grad.addColorStop(0.55, '#1b1d22');
  grad.addColorStop(1, '#101216');
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, 1024, 1024);
  // faint reference grid — a subtle cue of scale/ground plane, not a road marking
  ctx.strokeStyle = 'rgba(255,255,255,0.035)';
  ctx.lineWidth = 1;
  for (let i = 64; i < 1024; i += 64) {
    ctx.beginPath(); ctx.moveTo(i, 0); ctx.lineTo(i, 1024); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(0, i); ctx.lineTo(1024, i); ctx.stroke();
  }
  return c;
}
const studioFloorCanvas = makeStudioFloorTexture();
const studioFloorMap = new THREE.CanvasTexture(studioFloorCanvas);
studioFloorMap.colorSpace = THREE.SRGBColorSpace;

const floor = new THREE.Mesh(
  new THREE.CircleGeometry(620, 64),
  new THREE.MeshStandardMaterial({ map: studioFloorMap, roughness: 0.55, metalness: 0.12 })
);
floor.rotation.x = -Math.PI / 2;
floor.position.y = 0;
floor.receiveShadow = true;
scene.add(floor);

// soft dark surround so the floor doesn't just cut off at a hard edge
const surround = new THREE.Mesh(
  new THREE.RingGeometry(600, 1500, 64),
  new THREE.MeshStandardMaterial({ color: 0x0d0f12, roughness: 1 })
);
surround.rotation.x = -Math.PI / 2;
surround.position.y = -0.05;
surround.receiveShadow = true;
scene.add(surround);

/* ----------------------------------------------------------------------- */
/* Parts registry                                                          */
/* ----------------------------------------------------------------------- */

const PARTS = new Map();          // id -> { mesh, system, name, explodeOffset, basePos, baseScale }
const carGroup = new THREE.Group();
scene.add(carGroup);

const SYSTEMS = {
  engine:       { ka: 'ძრავის სისტემა',        en: 'Engine' },
  transmission: { ka: 'ტრანსმისია',             en: 'Transmission' },
  cooling:      { ka: 'გაცივების სისტემა',      en: 'Cooling' },
  electrical:   { ka: 'ელექტრო სისტემა',        en: 'Electrical' },
  fuel:         { ka: 'საწვავის სისტემა',       en: 'Fuel' },
  exhaust:      { ka: 'გამონაბოლქვის სისტემა',  en: 'Exhaust' },
  brakes:       { ka: 'სამუხრუჭე სისტემა',      en: 'Brakes' },
  suspension:   { ka: 'საბარავი და საჭე',       en: 'Suspension & Steering' },
  body:         { ka: 'კარკასი და სალონი',      en: 'Body & Interior' },
  hybrid:       { ka: 'ჰიბრიდული სისტემა',      en: 'Hybrid System' },
};

/** Stamps a material with the "base" values app-ui.js's material-state system reads to
 *  compute xray/selection/dim opacity and emissive. Any decorative sub-mesh (a bolt, a fin,
 *  a tread detail) needs this too, or it stays stuck at full brightness while the real part
 *  it's attached to fades around it. */
function trackMaterial(material) {
  material.userData.baseColor = material.color ? material.color.clone() : null;
  material.userData.baseOpacity = material.opacity;
  material.userData.baseTransparent = material.transparent;
  material.userData.baseEmissive = material.emissive ? material.emissive.clone() : new THREE.Color(0, 0, 0);
  material.userData.baseEmissiveIntensity = material.emissiveIntensity ?? 1;
}

function part(id, geometry, material, opts = {}) {
  const mesh = new THREE.Mesh(geometry, material);
  mesh.name = id;
  mesh.castShadow = opts.cast !== false;
  mesh.receiveShadow = opts.receive !== false;
  if (opts.pos) mesh.position.set(...opts.pos);
  if (opts.rot) mesh.rotation.set(...opts.rot);
  if (opts.scale) mesh.scale.set(...opts.scale);
  mesh.userData.partId = id;
  (opts.parent || carGroup).add(mesh);

  trackMaterial(material);

  PARTS.set(id, {
    mesh,
    id,
    system: opts.system || 'body',
    nameKa: opts.nameKa || id,
    nameEn: opts.nameEn || id,
    explodeOffset: new THREE.Vector3(...(opts.explode || [0, 0, 0])),
    basePos: mesh.position.clone(),
    xrayRole: opts.xrayRole || 'solid', // 'shell' | 'glass' | 'solid'
    labelAnchor: opts.labelAnchor || null,
    labelImportance: opts.labelImportance || 1,
  });
  return mesh;
}

function tubePart(id, points, radius, material, opts = {}) {
  const curve = new THREE.CatmullRomCurve3(points.map((p) => new THREE.Vector3(...p)));
  const geo = new THREE.TubeGeometry(curve, Math.max(16, points.length * 8), radius, 8, false);
  const mesh = part(id, geo, material, { ...opts, pos: [0, 0, 0] });
  // Tubes bake absolute coordinates into their geometry (not mesh.position), so the
  // per-shell vertical fit (app-ui.js's applyShellFit) can't move them by repositioning
  // the mesh — it rebuilds the geometry instead, using these raw points + radius.
  PARTS.get(id).curveSource = { points, radius };
  return mesh;
}

/** A rigid straight link between two real points in space (tie rods, brake lines' hard
 *  sections, anything that should visibly connect component A to component B instead of
 *  floating at a fixed Euler rotation). Length and orientation are derived from the two
 *  points, not guessed. */
function cylinderBetween(id, a, b, radius, material, opts = {}) {
  const pa = new THREE.Vector3(...a);
  const pb = new THREE.Vector3(...b);
  const mid = pa.clone().lerp(pb, 0.5);
  const length = pa.distanceTo(pb);
  const geo = new THREE.CylinderGeometry(radius, radius, length, 8);
  const mesh = part(id, geo, material, { ...opts, pos: [mid.x, mid.y, mid.z] });
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), pb.clone().sub(pa).normalize());
  // Like curveSource for tubes: the per-shell vertical fit needs the two real endpoints,
  // not just the mesh's own pivot, so it can re-derive both position AND orientation —
  // a rigid translate-only compression would leave a two-point link pointing at a stale
  // angle relative to the (independently compressed) parts it's supposed to connect.
  PARTS.get(id).linkSource = { a, b, radius };
  return mesh;
}

class HelixCurve extends THREE.Curve {
  constructor(radius, height, turns) { super(); this.radius = radius; this.height = height; this.turns = turns; }
  getPoint(t, target = new THREE.Vector3()) {
    const a = t * Math.PI * 2 * this.turns;
    return target.set(Math.cos(a) * this.radius, t * this.height - this.height / 2, Math.sin(a) * this.radius);
  }
}

/* ----------------------------------------------------------------------- */
/* Materials                                                                */
/* ----------------------------------------------------------------------- */

const M = {
  paint: () => new THREE.MeshPhysicalMaterial({ color: 0x323c48, metalness: 0.55, roughness: 0.3, clearcoat: 0.55, clearcoatRoughness: 0.18 }),
  glass: () => new THREE.MeshPhysicalMaterial({ color: 0x182028, metalness: 0.1, roughness: 0.05, transparent: true, opacity: 0.4 }),
  chassisMetal: () => new THREE.MeshStandardMaterial({ color: 0x4a4e55, metalness: 0.7, roughness: 0.55 }),
  engineMetal: () => new THREE.MeshStandardMaterial({ color: 0x5a5e64, metalness: 0.8, roughness: 0.42 }),
  darkMetal: () => new THREE.MeshStandardMaterial({ color: 0x33363c, metalness: 0.7, roughness: 0.5 }),
  chrome: () => new THREE.MeshStandardMaterial({ color: 0xdfe4e8, metalness: 1, roughness: 0.1 }),
  rubber: () => new THREE.MeshStandardMaterial({ color: 0x0c0d0f, metalness: 0.0, roughness: 0.92 }),
  plastic: (hex = 0x242730) => new THREE.MeshStandardMaterial({ color: hex, metalness: 0.08, roughness: 0.55 }),
  copper: () => new THREE.MeshStandardMaterial({ color: 0xb8742e, metalness: 0.85, roughness: 0.35 }),
  gold: () => new THREE.MeshStandardMaterial({ color: 0xcfa144, metalness: 0.9, roughness: 0.3 }),
  board: () => new THREE.MeshStandardMaterial({ color: 0x1e5c3a, metalness: 0.2, roughness: 0.6 }),
  fuelMetal: () => new THREE.MeshStandardMaterial({ color: 0x565a60, metalness: 0.6, roughness: 0.5 }),
  wire: () => new THREE.MeshStandardMaterial({ color: 0x15161a, metalness: 0.1, roughness: 0.75 }),
  // Orange is the real-world mandatory colour for high-voltage hybrid/EV cabling — keeping
  // it here instead of plain black/grey is what actually reads as "this is the hybrid
  // system" at a glance, not a label.
  hvCable: () => new THREE.MeshStandardMaterial({ color: 0xff7a1a, metalness: 0.1, roughness: 0.45, emissive: 0x2a1300, emissiveIntensity: 0.25 }),
};

/* ----------------------------------------------------------------------- */
/* Geometry — Body shell & chassis                                         */
/* ----------------------------------------------------------------------- */

function buildBody() {
  const rb = (w, h, d, seg = 3, r = 4) => new RoundedBoxGeometry(w, h, d, seg, r);

  // Shortened from 420 (which stuck 14-18 units out past both bumpers — visibly floating
  // structure outside the vehicle envelope) to end just inside the front/rear bumper faces.
  part('chassis_rail_left', new THREE.BoxGeometry(10, 8, 380), M.chassisMetal(), { pos: [-55, 15, 0], system: 'body' });
  part('chassis_rail_right', new THREE.BoxGeometry(10, 8, 380), M.chassisMetal(), { pos: [55, 15, 0], system: 'body' });
  part('front_subframe', new THREE.BoxGeometry(90, 10, 30), M.chassisMetal(), { pos: [0, 20, 120], system: 'body' });
  part('rear_subframe', new THREE.BoxGeometry(90, 10, 30), M.chassisMetal(), { pos: [0, 20, -120], system: 'body' });

  const shellOpts = { system: 'body', xrayRole: 'shell' };
  part('hood', rb(96, 8, 130, 2, 6), M.paint(), { ...shellOpts, pos: [0, 94, 150], rot: [-0.09, 0, 0], explode: [0, 55, 25] });
  part('roof', rb(148, 7, 165, 2, 6), M.paint(), { ...shellOpts, pos: [0, 149, 8], explode: [0, 70, 0] });
  part('trunk_lid', rb(90, 8, 86, 2, 6), M.paint(), { ...shellOpts, pos: [0, 101, -150], rot: [0.05, 0, 0], explode: [0, 50, -25] });
  part('front_bumper', rb(176, 28, 24, 3, 8), M.paint(), { ...shellOpts, pos: [0, 44, 192], explode: [0, 0, 55] });
  part('rear_bumper', rb(176, 28, 24, 3, 8), M.paint(), { ...shellOpts, pos: [0, 44, -196], explode: [0, 0, -55] });
  part('rocker_panel_left', rb(8, 18, 268, 2, 4), M.paint(), { ...shellOpts, pos: [-89, 26, 0], explode: [-35, 0, 0] });
  part('rocker_panel_right', rb(8, 18, 268, 2, 4), M.paint(), { ...shellOpts, pos: [89, 26, 0], explode: [35, 0, 0] });

  part('door_front_left', rb(8, 78, 108, 2, 6), M.paint(), { ...shellOpts, pos: [-90, 80, 58], explode: [-45, 0, 0] });
  part('door_front_right', rb(8, 78, 108, 2, 6), M.paint(), { ...shellOpts, pos: [90, 80, 58], explode: [45, 0, 0] });
  part('door_rear_left', rb(8, 76, 96, 2, 6), M.paint(), { ...shellOpts, pos: [-90, 79, -58], explode: [-45, 0, 0] });
  part('door_rear_right', rb(8, 76, 96, 2, 6), M.paint(), { ...shellOpts, pos: [90, 79, -58], explode: [45, 0, 0] });

  part('fender_front_left', rb(8, 68, 96, 2, 5), M.paint(), { ...shellOpts, pos: [-93, 60, 142], explode: [-40, 10, 20] });
  part('fender_front_right', rb(8, 68, 96, 2, 5), M.paint(), { ...shellOpts, pos: [93, 60, 142], explode: [40, 10, 20] });
  part('quarter_panel_rear_left', rb(8, 76, 96, 2, 5), M.paint(), { ...shellOpts, pos: [-93, 64, -147], explode: [-40, 10, -20] });
  part('quarter_panel_rear_right', rb(8, 76, 96, 2, 5), M.paint(), { ...shellOpts, pos: [93, 64, -147], explode: [40, 10, -20] });
  part('firewall', new THREE.BoxGeometry(174, 70, 8), M.chassisMetal(), { system: 'body', xrayRole: 'shell', pos: [0, 61, 96], explode: [0, -15, -10] });
  part('trunk_wall', new THREE.BoxGeometry(174, 76, 8), M.chassisMetal(), { system: 'body', xrayRole: 'shell', pos: [0, 64, -101], explode: [0, -15, 10] });
  part('floor_pan', new THREE.BoxGeometry(168, 6, 396), M.chassisMetal(), { system: 'body', xrayRole: 'shell', pos: [0, 13, 0], explode: [0, -35, 0] });

  part('windshield', new THREE.BoxGeometry(136, 5, 95), M.glass(), { ...shellOpts, xrayRole: 'glass', pos: [0, 122, 92], rot: [-0.62, 0, 0], explode: [0, 40, 15] });
  part('rear_window', new THREE.BoxGeometry(130, 5, 70), M.glass(), { ...shellOpts, xrayRole: 'glass', pos: [0, 120, -84], rot: [0.55, 0, 0], explode: [0, 40, -15] });
  part('side_window_left', new THREE.BoxGeometry(5, 46, 190), M.glass(), { ...shellOpts, xrayRole: 'glass', pos: [-88, 121, -2], explode: [-20, 20, 0] });
  part('side_window_right', new THREE.BoxGeometry(5, 46, 190), M.glass(), { ...shellOpts, xrayRole: 'glass', pos: [88, 121, -2], explode: [20, 20, 0] });

  part('dashboard', new THREE.BoxGeometry(150, 14, 26), M.plastic(), { system: 'body', pos: [0, 94, 92], rot: [0.12, 0, 0] });
  part('steering_wheel', new THREE.TorusGeometry(18, 2.6, 10, 24), M.plastic(0x16181c), { system: 'body', pos: [-34, 97, 112], rot: [Math.PI / 2 - 0.3, 0, 0] });
  part('steering_column', new THREE.CylinderGeometry(3, 3, 40, 10), M.darkMetal(), { system: 'body', pos: [-34, 85, 128], rot: [1.0, 0, 0] });
  part('instrument_cluster', new THREE.BoxGeometry(26, 8, 6), M.plastic(0x101216), { system: 'body', pos: [-34, 102, 100] });
  part('seat_driver', new THREE.BoxGeometry(42, 46, 44), M.plastic(0x1a1c20), { system: 'body', pos: [-36, 68, 20] });
  part('seat_passenger', new THREE.BoxGeometry(42, 46, 44), M.plastic(0x1a1c20), { system: 'body', pos: [36, 68, 20] });
  part('pedal_brake', new THREE.BoxGeometry(8, 3, 14), M.darkMetal(), { system: 'body', pos: [-40, 24, 118] });
  part('pedal_accelerator', new THREE.BoxGeometry(8, 3, 14), M.darkMetal(), { system: 'body', pos: [-28, 24, 118] });
}

/* ----------------------------------------------------------------------- */
/* Geometry — Engine, ignition, intake/exhaust manifolds                   */
/* ----------------------------------------------------------------------- */

function buildEngine() {
  // Narrower, rounded-casting proportions instead of a flat slab — a real inline-4 block
  // reads as a compact, taller-than-wide shape, not a wall filling the whole engine bay.
  part('engine_block', new RoundedBoxGeometry(68, 64, 84, 2, 5), M.engineMetal(), { system: 'engine', pos: [0, 58, 118], explode: [0, 0, 20] });
  part('oil_pan', new RoundedBoxGeometry(58, 10, 74, 2, 4), M.darkMetal(), { system: 'engine', pos: [0, 21, 118], explode: [0, -30, 0] });
  part('cylinder_head', new RoundedBoxGeometry(72, 18, 78, 2, 4), M.engineMetal(), { system: 'engine', pos: [0, 102, 118], explode: [0, 25, 0] });
  // Sits directly on the head, the way a real cam/valve cover does — the ignition coils
  // (positioned above) now plug down through it, which is exactly how coil-on-plug
  // ignition actually looks, instead of every component floating separately in open air.
  part('valve_cover', new RoundedBoxGeometry(62, 14, 72, 2, 5), M.plastic(0x1c1e22), { system: 'engine', pos: [0, 120, 118], explode: [0, 30, 0], labelImportance: 0.8 });
  part('oil_filter', new THREE.CylinderGeometry(8, 8, 15, 14), M.darkMetal(), { system: 'engine', pos: [38, 35, 142], rot: [0, 0, Math.PI / 2.4], explode: [20, -15, 20] });
  part('engine_mount_left', new THREE.BoxGeometry(14, 10, 14), M.darkMetal(), { system: 'engine', pos: [-46, 44, 118] });
  part('engine_mount_right', new THREE.BoxGeometry(14, 10, 14), M.darkMetal(), { system: 'engine', pos: [46, 44, 118] });

  const cylXs = [-24, -8, 8, 24];
  cylXs.forEach((cx, i) => {
    const n = i + 1;
    part(`spark_plug_${n}`, new THREE.CylinderGeometry(2.4, 2.8, 20, 10), M.darkMetal(), {
      system: 'engine', pos: [cx, 114, 118], explode: [0, 20, 0], labelImportance: 2,
    });
    part(`ignition_coil_${n}`, new THREE.BoxGeometry(11, 20, 9), M.plastic(0x16181c), {
      system: 'engine', pos: [cx, 134, 118], explode: [0, 48, 0], labelImportance: 2,
    });
  });

  part('intake_manifold', new RoundedBoxGeometry(58, 16, 24, 2, 3), M.plastic(0x2a2d33), { system: 'engine', pos: [0, 100, 148], explode: [0, 10, 28] });
  part('throttle_body', new THREE.CylinderGeometry(10, 10, 16, 14), M.engineMetal(), { system: 'engine', pos: [0, 102, 164], rot: [Math.PI / 2, 0, 0], explode: [0, 10, 36] });
  // Lowered and made shallower — at its old height/position it poked up through the hood.
  part('air_filter', new RoundedBoxGeometry(52, 18, 42, 2, 5), M.plastic(0x1d1f24), { system: 'engine', pos: [0, 83, 182], explode: [0, 10, 48] });

  cylXs.forEach((cx, i) => {
    tubePart(`intake_runner_${i + 1}`, [[cx, 104, 110], [cx, 102, 128], [0, 100, 140]], 3.2, M.plastic(0x2a2d33), { system: 'engine' });
  });

  // Electronically-monitored sensors — these are the actual OBD2-readable inputs, as
  // opposed to the surrounding mechanical parts they're bolted to. Kept as real,
  // independently selectable/clickable objects (not decals) so diagnostic relevance can
  // be taught at the component level, not just in text.
  part('maf_sensor', new THREE.CylinderGeometry(3, 3, 10, 12), M.darkMetal(), {
    system: 'engine', pos: [0, 90, 169], rot: [Math.PI / 2, 0, 0], explode: [0, 10, 20], labelImportance: 1.8,
  });
  part('map_sensor', new THREE.CylinderGeometry(2.6, 2.6, 9, 10), M.darkMetal(), {
    system: 'engine', pos: [39, 102, 148], rot: [0, 0, Math.PI / 2], explode: [15, 8, 10], labelImportance: 1.8,
  });
  part('coolant_temp_sensor', new THREE.CylinderGeometry(2.2, 2.2, 7, 10), M.darkMetal(), {
    system: 'engine', pos: [9, 86, 109], rot: [0, 0, Math.PI / 2], explode: [10, 10, 5], labelImportance: 1.8,
  });
  part('crank_position_sensor', new THREE.CylinderGeometry(2, 2, 8, 10), M.darkMetal(), {
    system: 'engine', pos: [39, 27, 158], rot: [0, 0, Math.PI / 2], explode: [15, -10, 10], labelImportance: 1.8,
  });
  part('cam_position_sensor', new THREE.CylinderGeometry(2, 2, 8, 10), M.darkMetal(), {
    system: 'engine', pos: [-35, 108, 158], rot: [Math.PI / 2, 0, 0], explode: [-15, 10, 10], labelImportance: 1.8,
  });

  part('alternator', new THREE.CylinderGeometry(15, 15, 20, 16), M.darkMetal(), { system: 'electrical', pos: [48, 62, 108], rot: [0, 0, Math.PI / 2], explode: [25, 0, -10] });
  part('starter_motor', new THREE.CylinderGeometry(10, 10, 16, 14), M.darkMetal(), { system: 'electrical', pos: [-46, 36, 100], rot: [0, 0, Math.PI / 2], explode: [-25, -10, -10] });
  part('battery', new RoundedBoxGeometry(26, 20, 22, 2, 3), M.plastic(0x1a1c20), { system: 'electrical', pos: [-62, 76, 155], explode: [-25, 10, 15] });
  part('battery_terminal_pos', new THREE.CylinderGeometry(2, 2, 4, 8), M.chrome(), { system: 'electrical', pos: [-67, 87, 160] });
  part('battery_terminal_neg', new THREE.CylinderGeometry(2, 2, 4, 8), M.darkMetal(), { system: 'electrical', pos: [-57, 87, 160] });
  part('ecu', new RoundedBoxGeometry(20, 7, 16, 2, 2), M.board(), { system: 'electrical', pos: [56, 70, 92], explode: [25, 10, -15] });
  part('fuse_box', new RoundedBoxGeometry(16, 10, 13, 2, 2), M.plastic(0x202329), { system: 'electrical', pos: [60, 82, 102], explode: [25, 15, -10] });

  tubePart('wiring_harness_main', [[-62, 80, 155], [0, 92, 125], [56, 72, 95], [0, 70, 60]], 2.3, M.wire(), { system: 'electrical' });
}

/* ----------------------------------------------------------------------- */
/* Geometry — Cooling, exhaust, transmission, fuel                         */
/* ----------------------------------------------------------------------- */

// A flat copper-colored slab read as "a copper block," not a radiator — real cores show a
// dense vertical tube/fin pattern. A tiled canvas texture gets that density cheaply (no
// extra geometry/draw calls), which matters since this whole scene already has mobile
// performance in mind.
function makeRadiatorFinTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 128;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#8a4e22';
  ctx.fillRect(0, 0, 128, 128);
  ctx.fillStyle = '#c9803e';
  for (let x = 3; x < 128; x += 14) ctx.fillRect(x, 0, 6, 128);
  ctx.strokeStyle = 'rgba(40,18,4,0.6)';
  ctx.lineWidth = 1;
  for (let y = 0; y < 128; y += 4) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(128, y); ctx.stroke(); }
  return c;
}
const radiatorFinMap = new THREE.CanvasTexture(makeRadiatorFinTexture());
radiatorFinMap.wrapS = radiatorFinMap.wrapT = THREE.RepeatWrapping;
radiatorFinMap.repeat.set(5, 4);
radiatorFinMap.colorSpace = THREE.SRGBColorSpace;

function buildCooling() {
  const radiatorCoreMat = new THREE.MeshStandardMaterial({ map: radiatorFinMap, roughness: 0.55, metalness: 0.45 });
  const radiator = part('radiator', new THREE.BoxGeometry(70, 50, 8), radiatorCoreMat, { system: 'cooling', pos: [0, 65, 172], explode: [0, 0, 60] });
  // Real cores have distinct end tanks the fin pattern doesn't belong on — plain copper
  // caps top and bottom, same treatment as the hybrid battery's ribs/inverter's fins.
  const tankMat = M.copper();
  trackMaterial(tankMat);
  const topTank = new THREE.Mesh(new THREE.BoxGeometry(74, 7, 11), tankMat);
  topTank.position.set(0, 28, 0);
  topTank.castShadow = true;
  radiator.add(topTank);
  const bottomTank = new THREE.Mesh(new THREE.BoxGeometry(74, 7, 11), tankMat);
  bottomTank.position.set(0, -28, 0);
  bottomTank.castShadow = true;
  radiator.add(bottomTank);
  part('cooling_fan_hub', new THREE.CylinderGeometry(6, 6, 8, 12), M.darkMetal(), { system: 'cooling', pos: [0, 65, 163], rot: [Math.PI / 2, 0, 0], explode: [0, 0, 40] });
  for (let i = 0; i < 6; i++) {
    const a = (i / 6) * Math.PI * 2;
    part(`cooling_fan_blade_${i + 1}`, new THREE.BoxGeometry(3, 26, 1), M.plastic(0x1c1e22), {
      system: 'cooling', pos: [Math.cos(a) * 15, 65 + Math.sin(a) * 15, 163], rot: [0, 0, a + Math.PI / 2], explode: [0, 0, 40], labelImportance: 0.3,
    });
  }
  part('coolant_reservoir', new THREE.CylinderGeometry(9, 9, 22, 14), new THREE.MeshPhysicalMaterial({ color: 0xcfe8e4, transparent: true, opacity: 0.55, roughness: 0.2 }), { system: 'cooling', pos: [36, 82, 150], explode: [20, 10, 20] });
  part('thermostat_housing', new THREE.BoxGeometry(14, 10, 12), M.engineMetal(), { system: 'cooling', pos: [0, 86, 110], explode: [0, 20, 0] });
  part('water_pump', new THREE.CylinderGeometry(11, 11, 13, 14), M.engineMetal(), { system: 'cooling', pos: [0, 54, 98], rot: [Math.PI / 2, 0, 0], explode: [0, -15, -10] });
  // A real upper hose runs a gentle, mostly-downhill path from the radiator top to the
  // thermostat housing — the old middle point sat higher than both ends, which the spline
  // turned into an unrealistic upward bulge instead of a believable hose.
  tubePart('hose_radiator_upper', [[0, 92, 170], [0, 89, 145], [0, 86, 112]], 3, M.rubber(), { system: 'cooling' });
  tubePart('hose_radiator_lower', [[0, 40, 168], [0, 42, 130], [0, 50, 100]], 3.4, M.rubber(), { system: 'cooling' });
}

function buildExhaust() {
  part('exhaust_manifold', new RoundedBoxGeometry(56, 10, 16, 2, 3), M.darkMetal(), { system: 'exhaust', pos: [0, 86, 95], explode: [0, -10, -15] });
  // Four individual curved header runners converging into the collector — the intake side
  // already had this branching-pipe look via intake_runner_*; the exhaust side was just a
  // plain box before, which doesn't read as a header at all.
  const cylXs = [-24, -8, 8, 24];
  cylXs.forEach((cx, i) => {
    tubePart(`exhaust_runner_${i + 1}`, [[cx, 95, 102], [cx * 0.5, 90, 97], [0, 86, 95]], 2.6, M.darkMetal(), { system: 'exhaust', labelImportance: 0.3 });
  });
  part('o2_sensor_upstream', new THREE.CylinderGeometry(3, 3.6, 10, 10), M.darkMetal(), { system: 'exhaust', pos: [10, 68, 88], rot: [0.3, 0, 0], explode: [0, -10, -25], labelImportance: 1.6 });
  part('catalytic_converter', new THREE.CylinderGeometry(13, 13, 40, 16), M.chrome(), { system: 'exhaust', pos: [0, 24, 20], rot: [Math.PI / 2, 0, 0], explode: [0, -20, -10] });
  part('o2_sensor_downstream', new THREE.CylinderGeometry(3, 3.6, 10, 10), M.darkMetal(), { system: 'exhaust', pos: [10, 26, -2], rot: [0.3, 0, 0], explode: [0, -20, -15] });
  part('muffler', new THREE.CylinderGeometry(16, 16, 60, 16), M.chrome(), { system: 'exhaust', pos: [0, 20, -112], rot: [Math.PI / 2, 0, 0], explode: [0, -25, -20] });
  part('exhaust_tip', new THREE.CylinderGeometry(6, 6.5, 14, 14), M.chrome(), { system: 'exhaust', pos: [22, 20, -176], rot: [Math.PI / 2, 0, 0], explode: [0, -10, -15] });
  tubePart('exhaust_pipe_front', [[0, 82, 90], [0, 50, 55], [0, 25, 20]], 7, M.darkMetal(), { system: 'exhaust' });
  tubePart('exhaust_pipe_rear', [[0, 24, 0], [0, 20, -70], [0, 20, -112]], 7, M.darkMetal(), { system: 'exhaust' });
  tubePart('exhaust_pipe_tail', [[0, 20, -142], [12, 20, -168], [22, 20, -176]], 6.5, M.darkMetal(), { system: 'exhaust' });
}

function buildTransmission() {
  part('transmission_housing', new RoundedBoxGeometry(68, 54, 78, 2, 5), M.engineMetal(), { system: 'transmission', pos: [0, 54, 18], explode: [0, 0, -30] });
  part('transmission_mount', new THREE.BoxGeometry(14, 8, 14), M.darkMetal(), { system: 'transmission', pos: [0, 24, 10], explode: [0, -15, -20] });
  part('differential', new RoundedBoxGeometry(42, 34, 36, 2, 4), M.engineMetal(), { system: 'transmission', pos: [0, 45, -130], explode: [0, 0, -40] });
  part('driveshaft', new THREE.CylinderGeometry(5.5, 5.5, 140, 14), M.darkMetal(), { system: 'transmission', pos: [0, 45, -55], rot: [Math.PI / 2, 0, 0], explode: [0, -20, 0] });
  part('axle_left', new THREE.CylinderGeometry(4.5, 4.5, 60, 10), M.darkMetal(), { system: 'transmission', pos: [-50, 45, -130], rot: [0, 0, Math.PI / 2], explode: [-20, 0, 0] });
  part('axle_right', new THREE.CylinderGeometry(4.5, 4.5, 60, 10), M.darkMetal(), { system: 'transmission', pos: [50, 45, -130], rot: [0, 0, Math.PI / 2], explode: [20, 0, 0] });

  part('tcm', new RoundedBoxGeometry(14, 6, 10, 2, 2), M.board(), {
    system: 'electrical', pos: [41, 58, 20], explode: [20, 10, -10], labelImportance: 1.6,
  });
}

function buildFuel() {
  part('fuel_tank', new RoundedBoxGeometry(94, 24, 62, 2, 5), M.fuelMetal(), { system: 'fuel', pos: [0, 24, -152], explode: [0, -20, -30] });
  part('fuel_pump', new THREE.CylinderGeometry(7, 7, 16, 12), M.darkMetal(), { system: 'fuel', pos: [10, 30, -152], explode: [10, -10, -40] });
  part('fuel_rail', new THREE.BoxGeometry(52, 6, 6), M.darkMetal(), { system: 'fuel', pos: [0, 109, 130], explode: [0, 20, 10] });
  const cylXs = [-24, -8, 8, 24];
  cylXs.forEach((cx, i) => {
    part(`injector_${i + 1}`, new THREE.CylinderGeometry(2.6, 2.6, 14, 10), M.darkMetal(), { system: 'fuel', pos: [cx, 101, 130], explode: [0, 14, 6], labelImportance: 1.3 });
  });
  tubePart('fuel_line_main', [[0, 24, -152], [0, 28, -40], [0, 60, 60], [0, 100, 120]], 2.6, M.rubber(), { system: 'fuel' });
}

/* ----------------------------------------------------------------------- */
/* Geometry — Brakes & suspension (per wheel)                              */
/* ----------------------------------------------------------------------- */

// Front wheels moved 18 units BACK (135→117) — the +18 forward attempt made the gap
// worse (confirmed directly by the user looking at the live page), so the real fender
// opening sits further back than the original 135, not further forward.
const WHEELS = [
  { key: 'fl', x: -80, z: 117, front: true },
  { key: 'fr', x: 80, z: 117, front: true },
  { key: 'rl', x: -80, z: -135, front: false },
  { key: 'rr', x: 80, z: -135, front: false },
];

// A flat featureless disc was the single most "obviously not real" part up close — real
// vented/drilled discs are immediately recognizable. Children are positioned in the disc's
// own local frame (its thickness runs along local Y, same as a plain CylinderGeometry's
// own axis) so they inherit the per-wheel rot/explode the disc mesh already has, for free.
const discVentMat = M.rubber();
const discHubMat = M.chassisMetal();
const discBoltMat = M.chrome();
[discVentMat, discHubMat, discBoltMat].forEach(trackMaterial);
function addDiscDetail(disc) {
  for (let i = 0; i < 10; i++) {
    const a = (i / 10) * Math.PI * 2;
    const hole = new THREE.Mesh(new THREE.CylinderGeometry(1.6, 1.6, 7, 10), discVentMat);
    hole.position.set(Math.cos(a) * 19, 0, Math.sin(a) * 19);
    disc.add(hole);
  }
  const hub = new THREE.Mesh(new THREE.CylinderGeometry(9, 9, 6.4, 20), discHubMat);
  hub.castShadow = true;
  disc.add(hub);
  for (let i = 0; i < 5; i++) {
    const a = (i / 5) * Math.PI * 2;
    const bolt = new THREE.Mesh(new THREE.CylinderGeometry(1.4, 1.4, 6.8, 10), discBoltMat);
    bolt.position.set(Math.cos(a) * 5.5, 0, Math.sin(a) * 5.5);
    disc.add(bolt);
  }
}

// A smooth black donut doesn't read as "tire" up close — a tiled tread texture gets a
// believable groove pattern without adding per-wheel geometry (same reasoning as the
// radiator fins). Torus UV: u wraps the wheel's circumference, v wraps the tube cross-
// section, so repeating densely in u and lightly in v gives tread blocks around the tire.
function makeTireTreadTexture() {
  const c = document.createElement('canvas');
  c.width = c.height = 256;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#0b0c0e';
  ctx.fillRect(0, 0, 256, 256);
  ctx.strokeStyle = '#000000';
  ctx.lineWidth = 10;
  for (let x = -256; x < 512; x += 30) {
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x + 140, 256);
    ctx.stroke();
  }
  return c;
}
const tireTreadMap = new THREE.CanvasTexture(makeTireTreadTexture());
tireTreadMap.wrapS = tireTreadMap.wrapT = THREE.RepeatWrapping;
tireTreadMap.repeat.set(14, 2);
tireTreadMap.colorSpace = THREE.SRGBColorSpace;
const tireMat = new THREE.MeshStandardMaterial({ map: tireTreadMap, roughness: 0.95, metalness: 0 });
trackMaterial(tireMat);

function buildWheelsBrakesSuspension() {
  WHEELS.forEach(({ key, x, z, front }) => {
    const side = x > 0 ? 1 : -1;
    const outward = [side * 25, 0, 0];

    // wheel (tire + rim)
    const wheelGroup = new THREE.Group();
    wheelGroup.position.set(x, 32, z);
    // Was rotation.z — a Z-axis rotation can never move the torus's own pole axis off of
    // world Z, so the tire's actual rolling axis stayed front-to-back instead of
    // side-to-side the whole time. Looked passable from a 3/4 angle (close enough to round
    // to not register), but from straight on it's a disc facing the wrong way — and the
    // brake disc (built completely separately, correctly axled on X) never matched it.
    // Rotating around Y instead is what actually swaps the pole axis onto world X.
    wheelGroup.rotation.y = Math.PI / 2;
    carGroup.add(wheelGroup);
    const tire = new THREE.Mesh(new THREE.TorusGeometry(32, 11, 14, 28), tireMat);
    tire.castShadow = tire.receiveShadow = true;
    wheelGroup.add(tire);
    // Slightly dished (radiusTop < radiusBottom) instead of a flat drum — reads as a real
    // alloy wheel's profile rather than a plain cylinder. Center cap + lug nuts give it a
    // finished look, matching the same bolt-pattern treatment as the brake disc's hub.
    const rimMat = M.chrome();
    const capMat = M.chrome();
    const nutMat = M.darkMetal();
    [rimMat, capMat, nutMat].forEach(trackMaterial);
    const rim = new THREE.Mesh(new THREE.CylinderGeometry(18, 20, 24, 20), rimMat);
    rim.rotation.x = Math.PI / 2;
    rim.castShadow = rim.receiveShadow = true;
    wheelGroup.add(rim);
    const cap = new THREE.Mesh(new THREE.CylinderGeometry(4, 4, 25, 16), capMat);
    cap.rotation.x = Math.PI / 2;
    cap.castShadow = true;
    wheelGroup.add(cap);
    for (let i = 0; i < 5; i++) {
      const a = (i / 5) * Math.PI * 2;
      const spoke = new THREE.Mesh(new THREE.BoxGeometry(5, 18, 4), rimMat);
      spoke.position.set(Math.cos(a) * 9, Math.sin(a) * 9, 0);
      spoke.rotation.z = a;
      wheelGroup.add(spoke);
      const nut = new THREE.Mesh(new THREE.CylinderGeometry(1.3, 1.3, 25, 8), nutMat);
      nut.rotation.x = Math.PI / 2;
      nut.position.set(Math.cos(a) * 9, Math.sin(a) * 9, 0);
      wheelGroup.add(nut);
    }
    wheelGroup.name = `wheel_${key}`;
    wheelGroup.userData.partId = `wheel_${key}`;
    PARTS.set(`wheel_${key}`, {
      mesh: wheelGroup, id: `wheel_${key}`, system: 'body',
      nameKa: `საბურავი (${wheelLabel(key)})`, nameEn: `Wheel & Tire (${key.toUpperCase()})`,
      explodeOffset: new THREE.Vector3(side * 18, 0, 0), basePos: wheelGroup.position.clone(), xrayRole: 'solid', labelImportance: 1,
    });

    // Disc is centered exactly on the wheel/hub axis — it was offset 10 units inboard
    // before, which on top of the earlier shell-fit Y mismatch made it look completely
    // detached from the wheel. Caliper/pad keep only a small realistic offset, enough to
    // sit just inboard of the disc face instead of a third of the way across the axle.
    const disc = part(`brake_disc_${key}`, new THREE.CylinderGeometry(26, 26, 6, 24), M.darkMetal(), {
      system: 'brakes', pos: [x, 32, z], rot: [0, 0, Math.PI / 2], explode: outward, labelImportance: 1.6,
    });
    addDiscDetail(disc);
    part(`brake_caliper_${key}`, new THREE.BoxGeometry(10, 18, 24), M.plastic(0xb23a3a), {
      system: 'brakes', pos: [x - side * 4, 42, z], explode: outward, labelImportance: 1.6,
    });
    part(`brake_pad_${key}`, new THREE.BoxGeometry(3, 10, 18), M.darkMetal(), {
      system: 'brakes', pos: [x - side * 2, 36, z], explode: outward, labelImportance: 0.3,
    });

    // Toned down from an oversized spring/shock that read as cartoonish — real coilovers
    // are proportionally much slimmer next to a road wheel.
    part(`coil_spring_${key}`, new THREE.TubeGeometry(new HelixCurve(12, 40, 6), 220, 2.2, 8, false), M.darkMetal(), {
      system: 'suspension', pos: [x - side * 25, 58, z], explode: [0, 25, 0], labelImportance: 1.3,
    });
    part(`shock_absorber_${key}`, new THREE.CylinderGeometry(3.2, 3.2, 42, 10), M.chrome(), {
      system: 'suspension', pos: [x - side * 25 + side * 10, 58, z], explode: [0, 25, 0], labelImportance: 1,
    });
    // A real control arm angles from a subframe pivot near the car's centerline out to the
    // ball joint — not a purely lateral bar floating a visible gap short of the knuckle.
    const armInboard = [x - side * 55, 24, z];
    const armOutboard = [x - side * 5, 32, z]; // == ball_joint_${key} below, so they actually meet
    cylinderBetween(`control_arm_${key}`, armInboard, armOutboard, 4, M.chassisMetal(), { system: 'suspension', explode: [0, -15, 0] });
    part(`ball_joint_${key}`, new THREE.SphereGeometry(6, 14, 14), M.darkMetal(), {
      system: 'suspension', pos: [x - side * 5, 32, z], explode: outward,
    });
    part(`wheel_hub_${key}`, new THREE.CylinderGeometry(9, 9, 10, 14), M.darkMetal(), {
      system: 'suspension', pos: [x, 32, z], rot: [0, 0, Math.PI / 2],
    });
    // Stabilizer drop link — a real anti-roll bar doesn't just float next to the
    // suspension, it's tied to it at each end.
    const stabBarZ = front ? 130 : -130;
    cylinderBetween(`stabilizer_link_${key}`, [side * 75, 34, stabBarZ], [x - side * 5, 32, z], 2, M.chassisMetal(), {
      system: 'suspension', labelImportance: 0.3,
    });

    // Tie rod: a real link between the steering rack's output end and the knuckle on
    // this wheel, not a free-floating bar at a guessed angle.
    if (front) {
      const rackEnd = [side * 31, 46, 120];
      const knuckleTop = [x - side * 8, 46, z];
      cylinderBetween(`tie_rod_${key}`, rackEnd, knuckleTop, 2.6, M.chassisMetal(), { system: 'suspension', explode: [0, 0, -10] });
    }
    // Brake line: routed like a real hard line — down from the master cylinder/ABS
    // junction to frame-rail height, back along the rail to this wheel's position, then
    // out to the caliper — instead of one straight rod floating between the two ends.
    const railX = side * 55;
    const railNearZ = front ? 138 : 95;
    tubePart(`brake_line_${key}`, [
      [22, 78, 138],
      [railX, 30, railNearZ],
      [railX, 30, z],
      [x - side * 4, 42, z],
    ], 1.5, M.darkMetal(), { system: 'brakes' });
  });

  part('steering_rack', new THREE.BoxGeometry(62, 9, 9), M.chassisMetal(), { system: 'suspension', pos: [0, 46, 120], explode: [0, 0, 25] });
  part('stabilizer_bar_front', new THREE.CylinderGeometry(3, 3, 150, 10), M.chassisMetal(), { system: 'suspension', pos: [0, 34, 130], rot: [0, 0, Math.PI / 2], explode: [0, -10, 0] });
  part('stabilizer_bar_rear', new THREE.CylinderGeometry(3, 3, 150, 10), M.chassisMetal(), { system: 'suspension', pos: [0, 34, -130], rot: [0, 0, Math.PI / 2], explode: [0, -10, 0] });

  part('master_cylinder', new THREE.CylinderGeometry(8, 8, 22, 14), M.darkMetal(), { system: 'brakes', pos: [22, 82, 148], rot: [Math.PI / 2, 0, 0], explode: [15, 15, 15] });
  part('brake_booster', new THREE.CylinderGeometry(16, 16, 26, 16), M.darkMetal(), { system: 'brakes', pos: [22, 80, 134], rot: [Math.PI / 2, 0, 0], explode: [15, 15, 5] });
  part('abs_module', new RoundedBoxGeometry(20, 14, 17, 2, 2), M.plastic(0x202329), { system: 'brakes', pos: [52, 66, 118], explode: [25, 10, 0] });
}

/* ----------------------------------------------------------------------- */
/* Geometry — Hybrid drivetrain (demo scope: battery pack, inverter, motor- */
/* generator, HV cabling) — a minimal but real representation, not a full  */
/* EV/hybrid vehicle variant. Kept as ordinary PARTS entries like everything*/
/* else, so it works with every mode, the fault demo, and diagnostic tiers.*/
/* ----------------------------------------------------------------------- */
function buildHybridSystem() {
  // Decorative sub-details (module ribs, heatsink fins, a connector boss) are added as
  // plain child meshes, not new PARTS entries — app.js's own traverse step below gives
  // every child the parent's partId automatically, so clicking a fin still selects "the
  // battery pack" or "the inverter" as one real component, not extra phantom parts.
  const battery = part('hybrid_battery_pack', new RoundedBoxGeometry(70, 16, 45, 2, 4), M.plastic(0x15171c), {
    system: 'hybrid', pos: [0, 70, -95], explode: [0, 20, -25], labelImportance: 1.8,
  });
  const ribMat = M.plastic(0x0c0d10);
  trackMaterial(ribMat);
  for (let i = -1; i <= 1; i++) {
    const rib = new THREE.Mesh(new THREE.BoxGeometry(70, 2, 3), ribMat);
    rib.position.set(0, 9, i * 13);
    rib.castShadow = true;
    battery.add(rib);
  }

  const pcu = part('power_control_unit', new RoundedBoxGeometry(26, 20, 22, 2, 3), M.darkMetal(), {
    system: 'hybrid', pos: [-55, 110, 145], explode: [-20, 15, 15], labelImportance: 1.8,
  });
  const finMat = M.chrome();
  trackMaterial(finMat);
  for (let i = 0; i < 5; i++) {
    const fin = new THREE.Mesh(new THREE.BoxGeometry(1.4, 18, 20), finMat);
    fin.position.set(-12 + i * 6, 0, 0);
    fin.castShadow = true;
    pcu.add(fin);
  }

  const motor = part('drive_motor_generator', new THREE.CylinderGeometry(20, 20, 26, 16), M.engineMetal(), {
    system: 'hybrid', pos: [0, 54, -15], rot: [Math.PI / 2, 0, 0], explode: [0, 0, -20], labelImportance: 1.2,
  });
  const connectorMat = M.plastic(0x16181c);
  trackMaterial(connectorMat);
  const connectorBox = new THREE.Mesh(new RoundedBoxGeometry(10, 8, 10, 2, 2), connectorMat);
  connectorBox.position.set(0, 18, 0);
  connectorBox.castShadow = true;
  motor.add(connectorBox);

  tubePart('hv_cable_main', [[0, 70, -95], [0, 40, -20], [0, 50, 20], [-55, 105, 145]], 3, M.hvCable(), { system: 'hybrid', labelImportance: 0.6 });
}

function wheelLabel(key) {
  return { fl: 'წინა მარცხენა', fr: 'წინა მარჯვენა', rl: 'უკანა მარცხენა', rr: 'უკანა მარჯვენა' }[key];
}

/* ----------------------------------------------------------------------- */
/* Build everything                                                         */
/* ----------------------------------------------------------------------- */

buildBody();
buildEngine();
buildCooling();
buildExhaust();
buildTransmission();
buildFuel();
buildWheelsBrakesSuspension();
buildHybridSystem();

for (const info of PARTS.values()) {
  info.mesh.traverse((o) => { if (o.isMesh) { o.userData.partId = o.userData.partId || info.id; } });
}

/* ----------------------------------------------------------------------- */
/* Rich part info (function / location / related)                          */
/* ----------------------------------------------------------------------- */

const CYL_ORDINAL = ['1-ლი', '2-ე', '3-ე', '4-ე'];

const RICH = {
  engine_block: { fn: 'ძრავის მთავარი ლითონის კორპუსი, რომელშიც მოძრაობს ცილინდრები და დგუშები.', loc: 'ძარის წინა ნაწილი, სავარძლებს ქვემოთ.', related: ['cylinder_head', 'oil_pan', 'crank_position_sensor'] },
  cylinder_head: { fn: 'ბლოკის ზედა ნაწილი, სადაც მდებარეობს სანთლები, ღუმელები და აირგამცემი არხები.', loc: 'ძრავის ბლოკის თავზე.', related: ['spark_plug_1', 'spark_plug_2', 'spark_plug_3', 'spark_plug_4', 'intake_manifold', 'cam_position_sensor'] },
  valve_cover: { fn: 'ხურავს საკარქველო მექანიზმს ზეთისგან დასაცავად — აალების კოჭები მასში გამავალი ხვრელებით ერთვის სანთლებს.', loc: 'ცილინდრის თავის თავზე.', related: ['cylinder_head', 'ignition_coil_1'] },
  oil_pan: { fn: 'ინახავს ძრავის ზეთს და უზრუნველყოფს მის ცირკულაციას.', loc: 'ძრავის ყველაზე დაბალი წერტილი.', related: ['oil_filter', 'engine_block'] },
  oil_filter: { fn: 'ფილტრავს ზეთს მინარევებისგან, სანამ ის ისევ ძრავაში შედის.', loc: 'ბლოკის გვერდზე, ქვემოთ.', related: ['oil_pan', 'engine_block'] },
  intake_manifold: { fn: 'ანაწილებს ჰაერ-საწვავის ნარევს ცილინდრებზე.', loc: 'ცილინდრის თავზე, ჰაერის ფილტრის მხარეს.', related: ['throttle_body', 'cylinder_head', 'fuel_rail', 'map_sensor'] },
  throttle_body: { fn: 'აკონტროლებს ძრავაში შესული ჰაერის რაოდენობას — საკუთარი პოზიციის სენსორი (TPS) პირდაპირ იკითხება OBD2-ით.', loc: 'საჰაერო ფილტრსა და საშუშების კოლექტორს შორის.', related: ['intake_manifold', 'air_filter', 'ecu', 'maf_sensor'] },
  air_filter: { fn: 'ასუფთავებს ძრავაში შემავალ ჰაერს მტვრისა და ნაწილაკებისგან.', loc: 'ძრავის განყოფილების წინა ნაწილი.', related: ['throttle_body'] },
  alternator: { fn: 'ძრავის მუშაობისას ტოკს გენერირებს და ბატარეას ტენის.', loc: 'ძრავის მარჯვენა მხარე, ქამრის მექანიზმთან.', related: ['battery', 'fuse_box'] },
  starter_motor: { fn: 'ძრავას ტრიალს აწვდის ძრავის გაშვების მომენტში.', loc: 'ძრავისა და ტრანსმისიის შეერთების ადგილი.', related: ['battery', 'transmission_housing'] },
  battery: { fn: '12 ვოლტიანი დენის წყარო ელექტრო სისტემისთვის და ძრავის გასაშვებად.', loc: 'ძრავის განყოფილების კუთხეში.', related: ['alternator', 'fuse_box', 'ecu'] },
  ecu: { fn: 'ძრავის მართვის ბლოკი — კითხულობს სენსორებს და მართავს აალებას, საწვავს და სხვა სისტემებს. OBD2 სკანერი ძირითადად ამ ბლოკთან საუბრობს.', loc: 'საწინა ფარის მახლობლად, ძრავის განყოფილებაში.', related: ['battery', 'fuse_box', 'spark_plug_1', 'maf_sensor', 'crank_position_sensor'] },
  maf_sensor: { fn: 'ზომავს ძრავაში შემავალი ჰაერის მასას — ECU-ს დასაფუძნებელ მონაცემს აწვდის სწორი საწვავის დოზირებისთვის. პირდაპირ იკითხება OBD2-ით.', loc: 'ჰაერის ფილტრსა და დროსელის სხეულს შორის, საშუშების მილზე.', related: ['air_filter', 'throttle_body', 'ecu'] },
  map_sensor: { fn: 'ზომავს საშუშების კოლექტორში წნევას — ძრავის დატვირთვის მაჩვენებელი ECU-სთვის. პირდაპირ იკითხება OBD2-ით.', loc: 'საშუშების კოლექტორზე.', related: ['intake_manifold', 'ecu'] },
  coolant_temp_sensor: { fn: 'ზომავს ძრავის გაცივების სითხის ტემპერატურას — გავლენას ახდენს საწვავის ნარევსა და მაცივარ ვენტილატორზე. პირდაპირ იკითხება OBD2-ით.', loc: 'თერმოსტატის სახლთან, ცილინდრის თავის მახლობლად.', related: ['thermostat_housing', 'ecu'] },
  crank_position_sensor: { fn: 'ადგენს მუხლის ლილვის ბრუნვის კუთხეს და სიჩქარეს — აალების დროის ძირეული სიგნალი. პირდაპირ იკითხება OBD2-ით.', loc: 'ძრავის ბლოკის წინა ნაწილი, ბორბლის მახლობლად.', related: ['engine_block', 'ecu', 'cam_position_sensor'] },
  cam_position_sensor: { fn: 'ადგენს ბარგის ლილვის პოზიციას — ECU-ს ეხმარება ცილინდრების თანმიმდევრობის დადგენაში. პირდაპირ იკითხება OBD2-ით.', loc: 'ცილინდრის თავი, დრო-მექანიზმის გარსაცმის მხარეს.', related: ['cylinder_head', 'crank_position_sensor', 'ecu'] },
  tcm: { fn: 'აკონტროლებს გადაცემების ავტომატურ გადართვას და ურთიერთქმედებს ECU-სთან. პირდაპირ იკითხება OBD2-ით (მხარდაჭერის შემთხვევაში).', loc: 'გადაცემათა კოლოფის გარეთ, გვერდზე.', related: ['transmission_housing', 'ecu'] },
  hybrid_battery_pack: { fn: 'ინახავს მაღალი ძაბვის ელექტროენერგიას ელექტრო ძრავისთვის. ბატარეის დატენვის დონე და საერთო მდგომარეობა უშუალოდ იკითხება ჰიბრიდული სისტემის სკანერით.', loc: 'საბარგულის იატაკის ქვეშ, უკანა სავარძლების უკან.', related: ['power_control_unit', 'hv_cable_main'] },
  power_control_unit: { fn: 'გარდაქმნის ბატარეის მუდმივ დენს ცვლად დენად ელექტრო ძრავისთვის და პირიქით — მუხრუჭებისას ენერგიის დასაბრუნებლად. საკუთარი გაუმართაობის კოდები აქვს.', loc: 'ძრავის განყოფილება, მაღალვოლტაჟიანი სისტემის გვერდით.', related: ['hybrid_battery_pack', 'drive_motor_generator'] },
  drive_motor_generator: { fn: 'ამოძრავებს მანქანას ელექტროენერგიით და მუხრუჭებისას მუშაობს გენერატორის რეჟიმში, ენერგიის დასაბრუნებლად ბატარეაში.', loc: 'გადაცემათა კოლოფთან ინტეგრირებული.', related: ['transmission_housing', 'power_control_unit'] },
  hv_cable_main: { fn: 'აკავშირებს ჰიბრიდულ ბატარეას, ინვერტორსა და ელექტრო ძრავას. ფორთოხლისფერი შეფერილობა მაღალი ძაბვის საერთაშორისო გაფრთხილების სტანდარტია. იზოლაციის დაზიანება იკითხება სისტემის მიერ.', loc: 'საბარგულიდან ძრავის განყოფილებამდე.', related: ['hybrid_battery_pack', 'power_control_unit'] },
  fuse_box: { fn: 'იცავს ელექტრო სქემებს გადატვირთვისგან.', loc: 'ბატარეასთან ახლოს.', related: ['battery', 'ecu'] },
  wiring_harness_main: { fn: 'მთავარი საკაბელო კონა, რომელიც აკავშირებს ელექტრო კომპონენტებს.', loc: 'ბატარეიდან ECU-მდე და ძრავამდე.', related: ['battery', 'ecu'] },
  radiator: { fn: 'აცივებს ძრავის გამცივებელ სითხეს ჰაერის ნაკადით.', loc: 'საბურავი ცხაურის უკან, წინ.', related: ['cooling_fan_hub', 'water_pump', 'hose_radiator_upper'] },
  cooling_fan_hub: { fn: 'ასუფთავებს ჰაერს რადიატორში, როცა მანქანა ნელა მოძრაობს ან დგას.', loc: 'რადიატორის უკან.', related: ['radiator'] },
  coolant_reservoir: { fn: 'გამცივებელი სითხის დამატებითი რეზერვუარი.', loc: 'ძრავის განყოფილების კუთხე.', related: ['radiator', 'thermostat_housing'] },
  thermostat_housing: { fn: 'აკონტროლებს გამცივებელი სითხის ცირკულაციას ტემპერატურის მიხედვით.', loc: 'ცილინდრის თავზე, გვერდზე.', related: ['water_pump', 'radiator', 'coolant_temp_sensor'] },
  water_pump: { fn: 'ატრიალებს გამცივებელ სითხეს მთელ სისტემაში.', loc: 'ძრავის წინა ნაწილი.', related: ['radiator', 'thermostat_housing'] },
  transmission_housing: { fn: 'გადასცემს ძრავის ბრუნს თვლებზე, გადაცემების მეშვეობით.', loc: 'ძრავის უკან.', related: ['driveshaft', 'differential', 'engine_block', 'tcm'] },
  driveshaft: { fn: 'გადასცემს ბრუნვას ტრანსმისიიდან დიფერენციალზე.', loc: 'ძარის ქვეშ, შუაში.', related: ['transmission_housing', 'differential'] },
  differential: { fn: 'ანაწილებს ბრუნვას უკანა თვლებზე, შესაძლებლობით სხვადასხვა სიჩქარით ბრუნონ.', loc: 'უკანა ხანჯლის ღერძის ცენტრში.', related: ['axle_left', 'axle_right', 'driveshaft'] },
  fuel_tank: { fn: 'ინახავს საწვავს.', loc: 'ძარის უკანა ნაწილის ქვეშ.', related: ['fuel_pump', 'fuel_line_main'] },
  fuel_pump: { fn: 'ტუმბავს საწვავს ავზიდან ძრავამდე, საჭირო წნევით.', loc: 'საწვავის ავზის შიგნით.', related: ['fuel_tank', 'fuel_rail'] },
  fuel_rail: { fn: 'ანაწილებს წნევაში მყოფ საწვავს ყველა ინჟექტორზე.', loc: 'საშუშების კოლექტორის თავზე.', related: ['injector_1', 'injector_2', 'injector_3', 'injector_4'] },
  exhaust_manifold: { fn: 'აგროვებს გამონაბოლქვ გაზებს ცილინდრებიდან ერთ მილში.', loc: 'ცილინდრის თავის გვერდზე.', related: ['catalytic_converter', 'o2_sensor_upstream'] },
  exhaust_runner_1: { fn: 'მიმართავს გამონაბოლქვ გაზებს 1-ლი ცილინდრიდან კოლექტორისკენ.', loc: 'ცილინდრის თავის გამონაბოლქვის მხარე.', related: ['exhaust_manifold'] },
  exhaust_runner_2: { fn: 'მიმართავს გამონაბოლქვ გაზებს 2-ე ცილინდრიდან კოლექტორისკენ.', loc: 'ცილინდრის თავის გამონაბოლქვის მხარე.', related: ['exhaust_manifold'] },
  exhaust_runner_3: { fn: 'მიმართავს გამონაბოლქვ გაზებს 3-ე ცილინდრიდან კოლექტორისკენ.', loc: 'ცილინდრის თავის გამონაბოლქვის მხარე.', related: ['exhaust_manifold'] },
  exhaust_runner_4: { fn: 'მიმართავს გამონაბოლქვ გაზებს 4-ე ცილინდრიდან კოლექტორისკენ.', loc: 'ცილინდრის თავის გამონაბოლქვის მხარე.', related: ['exhaust_manifold'] },
  o2_sensor_upstream: { fn: 'ზომავს ჟანგბადის შემცველობას გამონაბოლქვში კატალიზატორის წინ — ეხება საწვავის ნარევის კორექციას.', loc: 'საშუშების კოლექტორის შემდეგ.', related: ['exhaust_manifold', 'ecu', 'catalytic_converter'] },
  catalytic_converter: { fn: 'ანეიტრალებს მავნე ნივთიერებებს გამონაბოლქვში.', loc: 'საშუშების მილის შუა ნაწილი.', related: ['o2_sensor_upstream', 'o2_sensor_downstream'] },
  o2_sensor_downstream: { fn: 'ამოწმებს კატალიზატორის ეფექტურობას.', loc: 'კატალიზატორის შემდეგ.', related: ['catalytic_converter'] },
  muffler: { fn: 'ამცირებს ძრავის ხმაურს.', loc: 'ძარის უკანა ნაწილი.', related: ['exhaust_tip'] },
  exhaust_tip: { fn: 'გამონაბოლქვის სისტემის გამოსასვლელი წერტილი.', loc: 'უკანა ბამპერის ქვეშ.', related: ['muffler'] },
  master_cylinder: { fn: 'გადასცემს მუხრუჭის პედლის ძალას ჰიდრავლიკურ წნევად.', loc: 'საჭის სვეტის მახლობლად, ფარდა კედელზე.', related: ['brake_booster', 'pedal_brake'] },
  brake_booster: { fn: 'ამცირებს მუხრუჭზე საჭირო ფიზიკურ ძალას.', loc: 'საწოლი ცილინდრის უკან.', related: ['master_cylinder'] },
  abs_module: { fn: 'აკონტროლებს თვლების ბლოკირებას მკვეთრი დამუხრუჭებისას.', loc: 'ძრავის განყოფილების კუთხე.', related: ['brake_disc_fl', 'brake_disc_fr'] },
  steering_rack: { fn: 'თარგმნის საჭის ტრიალს წინა თვლების მობრუნებაში.', loc: 'წინა ღერძის უკან.', related: ['tie_rod_fl', 'tie_rod_fr', 'steering_wheel'] },
  steering_wheel: { fn: 'მძღოლის მთავარი მართვის ინტერფეისი საჭესთან.', loc: 'სავარძელის წინ.', related: ['steering_column', 'steering_rack'] },
  dashboard: { fn: 'აერთიანებს საჭის წინა პანელს — ხელსაწყოები, ეკრანი, საჰაერო გამწოვები.', loc: 'სალონის წინა ნაწილი.', related: ['instrument_cluster', 'steering_wheel'] },
  seat_driver: { fn: 'მძღოლის სავარძელი.', loc: 'სალონის მარცხენა წინა ნაწილი.', related: ['dashboard'] },
};

for (let i = 1; i <= 4; i++) {
  RICH[`spark_plug_${i}`] = {
    fn: 'აალებს შეკუმშულ ჰაერ-საწვავის ნარევს კამერაში, ნაპერწკლის მეშვეობით.',
    loc: `ცილინდრი ${CYL_ORDINAL[i - 1]}, ცილინდრის თავზე.`,
    related: [`ignition_coil_${i}`, 'cylinder_head', 'ecu'],
  };
  RICH[`ignition_coil_${i}`] = {
    fn: 'ქმნის მაღალ ძაბვას შესაბამისი სანთლის ასანთებლად.',
    loc: `სანთელი ${i}-ის თავზე.`,
    related: [`spark_plug_${i}`, 'ecu', 'battery'],
  };
  RICH[`injector_${i}`] = {
    fn: 'ასხურებს ზუსტად დოზირებულ საწვავს საშუშების არხში.',
    loc: `ცილინდრი ${CYL_ORDINAL[i - 1]}-ის საშუშების არხი.`,
    related: ['fuel_rail', 'ecu'],
  };
  RICH[`intake_runner_${i}`] = {
    fn: 'მიმართავს ჰაერ-საწვავის ნარევს კოლექტორიდან კონკრეტულ ცილინდრამდე.',
    loc: `კოლექტორსა და ცილინდრი ${CYL_ORDINAL[i - 1]}-ს შორის.`,
    related: ['intake_manifold', 'cylinder_head'],
  };
}

WHEELS.forEach(({ key }) => {
  const lbl = wheelLabel(key);
  RICH[`brake_disc_${key}`] = { fn: 'მბრუნავი ლითონის დისკი — სამუხრუჭე ზედაპირი.', loc: `${lbl} თვალი.`, related: [`brake_caliper_${key}`, `wheel_hub_${key}`] };
  RICH[`brake_caliper_${key}`] = { fn: 'კუმშავს სამუხრუჭე ხუნდებს დისკზე, რათა შეანელოს თვალი.', loc: `${lbl} თვალი.`, related: [`brake_disc_${key}`, 'master_cylinder'] };
  RICH[`brake_pad_${key}`] = { fn: 'ცვეთადი ხუნდი, რომელიც უშუალოდ ეხება დისკს დამუხრუჭებისას. ფიზიკური ნაწილი — OBD2 პირდაპირ ვერ ამოწმებს ცვეთას.', loc: `${lbl} სუპორტის შიგნით.`, related: [`brake_caliper_${key}`, `brake_disc_${key}`] };
  RICH[`stabilizer_link_${key}`] = { fn: 'აკავშირებს სტაბილიზატორის ღერძს საბარავთან, მოხვევისას გვერდულ დახრას ამცირებს.', loc: `${lbl} საბარავი.`, related: [`control_arm_${key}`] };
  RICH[`coil_spring_${key}`] = { fn: 'ამსუბუქებს ბიძგებს გზის ზედაპირიდან.', loc: `${lbl} საბარავი.`, related: [`shock_absorber_${key}`, `control_arm_${key}`] };
  RICH[`shock_absorber_${key}`] = { fn: 'აკონტროლებს ზამბარის რხევას და ასწორებს სავალ ნაწილს.', loc: `${lbl} საბარავი.`, related: [`coil_spring_${key}`] };
  RICH[`control_arm_${key}`] = { fn: 'აკავშირებს თვალს ძარასთან და განსაზღვრავს მის გეომეტრიას.', loc: `${lbl} საბარავი.`, related: [`ball_joint_${key}`, `coil_spring_${key}`] };
  RICH[`brake_line_${key}`] = { fn: 'მაგარი ლითონის მილი, რომელიც გადასცემს ჰიდრავლიკურ წნევას მთავარი ცილინდრიდან ამ თვლის სუპორტამდე. ფიზიკური ნაწილი — OBD2 პირდაპირ ვერ ამოწმებს.', loc: `შასის გასწვრივ, ${lbl} თვალისკენ.`, related: ['master_cylinder', `brake_caliper_${key}`] };
  RICH[`ball_joint_${key}`] = { fn: 'აკავშირებს საბარავ ბერკეტს კუზლაკთან და საშუალებას აძლევს თვალს საჭესთან ერთად მოტრიალდეს.', loc: `${lbl} საბარავი.`, related: [`control_arm_${key}`, `wheel_hub_${key}`] };
  RICH[`wheel_hub_${key}`] = { fn: 'თვლის მბრუნავი ცენტრი — აკავშირებს დისკს და საბურავს ღერძთან.', loc: `${lbl} თვალი.`, related: [`brake_disc_${key}`] };
  if (key === 'fl' || key === 'fr') {
    RICH[`tie_rod_${key}`] = { fn: 'გადასცემს საჭის მოძრაობას საჭის რეიკიდან თვლის კუზლაკზე, თვლის მობრუნებისთვის.', loc: `საჭის რეიკასა და ${lbl} თვალს შორის.`, related: ['steering_rack', `ball_joint_${key}`] };
  }
});

function nameForFallback(id) {
  const words = id.replace(/_/g, ' ');
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function getInfo(id) {
  if (RICH[id]) return RICH[id];
  return { fn: 'ავტომობილის სისტემის ნაწილი — დეტალური აღწერა მალე დაემატება.', loc: 'იხილეთ 3D მოდელში მონიშნული მდებარეობა.', related: [] };
}

/* name labels (Georgian) for ids not already given nameKa when created */
const NAME_KA_OVERRIDES = {
  spark_plug_1: '1-ლი სანთელი', spark_plug_2: '2-ე სანთელი', spark_plug_3: '3-ე სანთელი', spark_plug_4: '4-ე სანთელი',
  ignition_coil_1: '1-ლი აალების კოჭა', ignition_coil_2: '2-ე აალების კოჭა', ignition_coil_3: '3-ე აალების კოჭა', ignition_coil_4: '4-ე აალების კოჭა',
  injector_1: '1-ლი ინჟექტორი', injector_2: '2-ე ინჟექტორი', injector_3: '3-ე ინჟექტორი', injector_4: '4-ე ინჟექტორი',
  intake_runner_1: '1-ლი საშუშების არხი', intake_runner_2: '2-ე საშუშების არხი', intake_runner_3: '3-ე საშუშების არხი', intake_runner_4: '4-ე საშუშების არხი',
  maf_sensor: 'ჰაერის მასის სენსორი (MAF)', map_sensor: 'საშუშების წნევის სენსორი (MAP)',
  coolant_temp_sensor: 'გაცივ. სითხის ტემპერატურის სენსორი', crank_position_sensor: 'მუხლის ლილვის პოზიციის სენსორი',
  cam_position_sensor: 'ბარგის ლილვის პოზიციის სენსორი', tcm: 'ტრანსმისიის მართვის ბლოკი (TCM)',
  hybrid_battery_pack: 'ჰიბრიდული ბატარეის პაკეტი', power_control_unit: 'გამძლავრების მართვის ბლოკი (ინვერტორი)',
  drive_motor_generator: 'ელექტრო ძრავა-გენერატორი', hv_cable_main: 'მაღალი ძაბვის კაბელი',
  engine_block: 'ძრავის ბლოკი', cylinder_head: 'ცილინდრის თავი', valve_cover: 'საკარქველო სახურავი', oil_pan: 'ზეთის ფსკერი', oil_filter: 'ზეთის ფილტრი',
  exhaust_runner_1: '1-ლი გამონაბოლქვის არხი', exhaust_runner_2: '2-ე გამონაბოლქვის არხი', exhaust_runner_3: '3-ე გამონაბოლქვის არხი', exhaust_runner_4: '4-ე გამონაბოლქვის არხი',
  intake_manifold: 'საშუშების კოლექტორი', throttle_body: 'დროსელის სხეული', air_filter: 'ჰაერის ფილტრი',
  alternator: 'გენერატორი', starter_motor: 'სტარტერი', battery: 'აკუმულატორი', ecu: 'ძრავის კომპიუტერი (ECU)', fuse_box: 'საცვენი ბლოკი',
  wiring_harness_main: 'მთავარი საკაბელო კონა', radiator: 'რადიატორი', cooling_fan_hub: 'გამაცივებელი ვენტილატორი',
  coolant_reservoir: 'გამცივებლის რეზერვუარი', thermostat_housing: 'თერმოსტატი', water_pump: 'წყლის ტუმბო',
  transmission_housing: 'გადაცემათა კოლოფი', driveshaft: 'კარდანული ლილვი', differential: 'დიფერენციალი',
  axle_left: 'მარცხენა ნახევარღერძი', axle_right: 'მარჯვენა ნახევარღერძი', transmission_mount: 'კოლოფის საყრდენი',
  fuel_tank: 'საწვავის ავზი', fuel_pump: 'ბენზინის ტუმბო', fuel_rail: 'საწვავის რეიკა', fuel_line_main: 'საწვავის მილი',
  exhaust_manifold: 'საშუშების კოლექტორი (გამონაბოლქვი)', o2_sensor_upstream: 'ლამბდა ზონდი (წინა)',
  catalytic_converter: 'კატალიზატორი', o2_sensor_downstream: 'ლამბდა ზონდი (უკანა)', muffler: 'საშუმავებელი', exhaust_tip: 'გამონაბოლქვის მილის ბოლო',
  master_cylinder: 'მთავარი სამუხრუჭე ცილინდრი', brake_booster: 'მუხრუჭის გამაძლიერებელი', abs_module: 'ABS ბლოკი',
  steering_rack: 'საჭის რეიკა', steering_wheel: 'საჭე', steering_column: 'საჭის სვეტი', dashboard: 'პანელი', instrument_cluster: 'ხელსაწყოების პანელი',
  seat_driver: 'მძღოლის სავარძელი', seat_passenger: 'მგზავრის სავარძელი', pedal_brake: 'მუხრუჭის პედალი', pedal_accelerator: 'გაზის პედალი',
  hood: 'კაპოტი', roof: 'სახურავი', trunk_lid: 'საბარგულის ხუფი', front_bumper: 'წინა ბამპერი', rear_bumper: 'უკანა ბამპერი',
  rocker_panel_left: 'მარცხენა ბოძი (ქვედა)', rocker_panel_right: 'მარჯვენა ბოძი (ქვედა)',
  door_front_left: 'წინა მარცხენა კარი', door_front_right: 'წინა მარჯვენა კარი', door_rear_left: 'უკანა მარცხენა კარი', door_rear_right: 'უკანა მარჯვენა კარი',
  windshield: 'წინა მინა', rear_window: 'უკანა მინა', side_window_left: 'მარცხენა მინა', side_window_right: 'მარჯვენა მინა',
  chassis_rail_left: 'მარცხენა ჩარჩო', chassis_rail_right: 'მარჯვენა ჩარჩო', front_subframe: 'წინა ქვეჩარჩო', rear_subframe: 'უკანა ქვეჩარჩო',
  fender_front_left: 'წინა მარცხენა ფრთა', fender_front_right: 'წინა მარჯვენა ფრთა',
  quarter_panel_rear_left: 'უკანა მარცხენა ფრთა', quarter_panel_rear_right: 'უკანა მარჯვენა ფრთა',
  firewall: 'ცეცხლგამძლე ტიხარი', trunk_wall: 'საბარგულის კედელი', floor_pan: 'ძარის იატაკი',
  battery_terminal_pos: 'ბატარეის (+) კლემა', battery_terminal_neg: 'ბატარეის (−) კლემა',
  stabilizer_bar_front: 'წინა სტაბილიზატორი', stabilizer_bar_rear: 'უკანა სტაბილიზატორი',
  exhaust_pipe_front: 'გამონაბოლქვის მილი (წინა)', exhaust_pipe_rear: 'გამონაბოლქვის მილი (უკანა)', exhaust_pipe_tail: 'გამონაბოლქვის მილი (ბოლო)',
  hose_radiator_upper: 'რადიატორის ზედა შლანგი', hose_radiator_lower: 'რადიატორის ქვედა შლანგი',
  engine_mount_left: 'ძრავის საყრდენი (მარცხენა)', engine_mount_right: 'ძრავის საყრდენი (მარჯვენა)',
};
WHEELS.forEach(({ key }) => {
  const lbl = wheelLabel(key);
  NAME_KA_OVERRIDES[`brake_disc_${key}`] = `სამუხრუჭე დისკი (${lbl})`;
  NAME_KA_OVERRIDES[`brake_caliper_${key}`] = `სუპორტი (${lbl})`;
  NAME_KA_OVERRIDES[`coil_spring_${key}`] = `ზამბარა (${lbl})`;
  NAME_KA_OVERRIDES[`shock_absorber_${key}`] = `ამორტიზატორი (${lbl})`;
  NAME_KA_OVERRIDES[`control_arm_${key}`] = `ბერკეტი (${lbl})`;
  NAME_KA_OVERRIDES[`ball_joint_${key}`] = `შარავო (${lbl})`;
  NAME_KA_OVERRIDES[`wheel_hub_${key}`] = `ჰაბი (${lbl})`;
  NAME_KA_OVERRIDES[`brake_line_${key}`] = `მუხრუჭის მილი (${lbl})`;
  NAME_KA_OVERRIDES[`brake_pad_${key}`] = `სამუხრუჭე ხუნდი (${lbl})`;
  NAME_KA_OVERRIDES[`stabilizer_link_${key}`] = `სტაბილიზატორის ბმული (${lbl})`;
  if (key === 'fl' || key === 'fr') NAME_KA_OVERRIDES[`tie_rod_${key}`] = `საჭის წევა (${lbl})`;
});

/* ----------------------------------------------------------------------- */
/* Diagnostic relevance — three conceptual tiers, used by the UI to decide */
/* visual prominence and to show the user what a standard OBD2 adapter can */
/* actually see. Tier 1 = has its own dedicated OBD2 signal/PID/DTC. Tier 2 */
/* = not read directly, but its health shows up in another sensor's data   */
/* (e.g. a vacuum leak shows up as a fuel-trim code, not as its own code). */
/* Everything else defaults to tier 3 = physical-only, invisible to OBD2.  */
/* ----------------------------------------------------------------------- */
const DIAG_TIER = {
  ecu: 1, maf_sensor: 1, map_sensor: 1, coolant_temp_sensor: 1, crank_position_sensor: 1, cam_position_sensor: 1, tcm: 1,
  throttle_body: 1, catalytic_converter: 1, abs_module: 1, thermostat_housing: 1,
  o2_sensor_upstream: 1, o2_sensor_downstream: 1,
  spark_plug_1: 1, spark_plug_2: 1, spark_plug_3: 1, spark_plug_4: 1,
  ignition_coil_1: 1, ignition_coil_2: 1, ignition_coil_3: 1, ignition_coil_4: 1,
  injector_1: 1, injector_2: 1, injector_3: 1, injector_4: 1,
  alternator: 2, battery: 2, radiator: 2, cooling_fan_hub: 2, water_pump: 2,
  fuel_pump: 2, fuel_rail: 2, intake_manifold: 2, exhaust_manifold: 2,
  hybrid_battery_pack: 1, power_control_unit: 1, drive_motor_generator: 2, hv_cable_main: 2,
};
function tierOf(id) { return DIAG_TIER[id] || 3; }

/* Label-importance overrides, applied after every part exists — this is where diagnostic
 * relevance actually changes what you see: tier-1 sensors get bumped up, and the brake/
 * suspension sub-parts the user specifically does not want competing for attention with
 * real diagnostic components (OBD2 cannot read a brake pad or a ball joint) get turned
 * down. Geometry and mechanical relationships are untouched — only label priority. */
const LABEL_IMPORTANCE_OVERRIDES = {
  ecu: 2, throttle_body: 1.6, catalytic_converter: 1.6, abs_module: 1.6, thermostat_housing: 1.4,
  injector_1: 1.8, injector_2: 1.8, injector_3: 1.8, injector_4: 1.8,
  o2_sensor_upstream: 1.8, o2_sensor_downstream: 1.8,
  master_cylinder: 0.5, brake_booster: 0.5, battery_terminal_pos: 0, battery_terminal_neg: 0,
  engine_mount_left: 0.3, engine_mount_right: 0.3,
  stabilizer_bar_front: 0.3, stabilizer_bar_rear: 0.3,
  axle_left: 0.5, axle_right: 0.5, driveshaft: 0.5, differential: 0.6, transmission_mount: 0.3,
};
WHEELS.forEach(({ key }) => {
  LABEL_IMPORTANCE_OVERRIDES[`brake_disc_${key}`] = 0.65;
  LABEL_IMPORTANCE_OVERRIDES[`brake_caliper_${key}`] = 0.65;
  LABEL_IMPORTANCE_OVERRIDES[`brake_line_${key}`] = 0.3;
  LABEL_IMPORTANCE_OVERRIDES[`coil_spring_${key}`] = 0.5;
  LABEL_IMPORTANCE_OVERRIDES[`shock_absorber_${key}`] = 0.5;
  LABEL_IMPORTANCE_OVERRIDES[`control_arm_${key}`] = 0.4;
  LABEL_IMPORTANCE_OVERRIDES[`ball_joint_${key}`] = 0;
  LABEL_IMPORTANCE_OVERRIDES[`wheel_hub_${key}`] = 0;
  LABEL_IMPORTANCE_OVERRIDES[`tie_rod_${key}`] = 0.4;
});

for (const [id, info] of PARTS.entries()) {
  if (NAME_KA_OVERRIDES[id]) info.nameKa = NAME_KA_OVERRIDES[id];
  if (info.nameEn === id) info.nameEn = nameForFallback(id);
  const rich = getInfo(id);
  info.fn = rich.fn;
  info.loc = rich.loc;
  info.related = rich.related;
  info.diagTier = tierOf(id);
  if (LABEL_IMPORTANCE_OVERRIDES[id] !== undefined) info.labelImportance = LABEL_IMPORTANCE_OVERRIDES[id];
}

/* ----------------------------------------------------------------------- */
/* Continue in app-ui.js (selection, modes, camera, labels, fault demo)    */
/* app-ui.js is loaded as a second module script, after this one finishes, */
/* so the global below is already populated by the time it runs.          */
/* ----------------------------------------------------------------------- */

window.__OBD360_SCENE__ = { scene, camera, renderer, controls, carGroup, PARTS, SYSTEMS, WHEELS };
