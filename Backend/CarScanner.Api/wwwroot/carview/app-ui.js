import * as THREE from 'three';
import { ensureShellLoaded, shellGroups, shellMeta, SHELL_DEFS } from './shells.js';

const { scene, camera, renderer, controls, carGroup, PARTS, SYSTEMS, WHEELS } = window.__OBD360_SCENE__;
window.__OBD360_SCENE__.shellGroups = shellGroups;

const stage = document.getElementById('stage');
const labelLayer = document.getElementById('label-layer');
const leaderSvg = document.getElementById('leader-svg');
const loadingEl = document.getElementById('loading');

const state = {
  mode: 'normal',          // normal | xray | cutaway — exploded is a transient tween, not a resting mode
  focusSystem: null,       // independent of mode: dims every other system, works combined with any mode
  diagView: false,         // independent of mode: dims non-OBD2-readable parts regardless of system
  selectedId: null,
  relatedIds: [],
  faultActive: false,
  explode: 0,
  xray: parseFloat(document.getElementById('xray-slider').value),
  labelsOn: true,
  shell: 'sloyd',
};

const PROCEDURAL_SHELL_IDS = Array.from(PARTS.keys()).filter((id) => PARTS.get(id).xrayRole !== 'solid');
// A sourced shell (Sloyd/Quaternius) already has its own wheels baked into the one body
// mesh — our own tire/rim objects are only for the procedural shell, and left visible
// they double up with the source model's wheels and stick out past its (different) track
// width. Brake discs/calipers/suspension stay visible always — those are real info, not skin.
const PROCEDURAL_WHEEL_IDS = WHEELS.map((w) => `wheel_${w.key}`);
const SHELL_KEYS = Object.keys(SHELL_DEFS);
const shellClipPlanes = {};
SHELL_KEYS.forEach((key) => { shellClipPlanes[key] = new THREE.Plane(new THREE.Vector3(0, -1, 0), 118); });

/* ----------------------------------------------------------------------- */
/* One-time render-order setup (transparent shell/glass draw after solids) */
/* ----------------------------------------------------------------------- */
for (const info of PARTS.values()) {
  if (info.xrayRole !== 'solid') {
    info.mesh.traverse((o) => { if (o.isMesh) o.renderOrder = info.xrayRole === 'shell' ? 10 : 8; });
  }
}

function eachMaterial(info, cb) {
  info.mesh.traverse((o) => { if (o.isMesh && o.material) cb(o.material); });
}

function isVisible(obj) {
  let o = obj;
  while (o) { if (o.visible === false) return false; o = o.parent; }
  return true;
}

function pulse(speed = 2.2) {
  return (Math.sin(performance.now() / 1000 * speed) + 1) / 2;
}

/* ----------------------------------------------------------------------- */
/* Material / visibility state                                             */
/* ----------------------------------------------------------------------- */

const CUTAWAY_HIDDEN = ['hood', 'roof', 'door_front_left', 'trunk_lid', 'windshield', 'fender_front_left'];

function applyCutawayVisibility() {
  const cutaway = state.mode === 'cutaway';
  if (state.shell === 'procedural') {
    CUTAWAY_HIDDEN.forEach((id) => { const info = PARTS.get(id); if (info) info.mesh.visible = !cutaway; });
  }
  setShellClipping();
}

function setShellClipping() {
  SHELL_KEYS.forEach((key) => {
    const active = state.shell === key && state.mode === 'cutaway';
    shellGroups[key].traverse((o) => {
      if (!o.isMesh) return;
      const mats = Array.isArray(o.material) ? o.material : [o.material];
      mats.forEach((m) => { m.clippingPlanes = active ? [shellClipPlanes[key]] : null; });
    });
  });
}

/** The model decision is final — TopoSphere Sedan via Sloyd — so the earlier 3-way
 *  comparison picker and the realistic/schematic toggle are both retired; only the
 *  realistic shell is ever shown. The CC BY 4.0 credit is still required by the license,
 *  but lives in the "ⓘ" button's tooltip in the topbar (static HTML) instead of a
 *  persistent text line in the panel. */
function setShell(key) {
  state.shell = key;
  PROCEDURAL_SHELL_IDS.forEach((id) => { PARTS.get(id).mesh.visible = key === 'procedural'; });
  PROCEDURAL_WHEEL_IDS.forEach((id) => { PARTS.get(id).mesh.visible = key === 'procedural'; });
  SHELL_KEYS.forEach((k) => { shellGroups[k].visible = k === key; });
  applyShellFit(key);
  applyExplodePositions();
  applyCutawayVisibility();
  applyMaterialState();

  if (key !== 'procedural' && SHELL_DEFS[key]) {
    ensureShellLoaded(scene, key).then((info) => {
      if (info?.error) { shellMeta[key].triangles = 'ვერ ჩაიტვირთა'; }
      else {
        if (info?.triangles) shellMeta[key].triangles = info.triangles.toLocaleString();
        if (info?.height != null) shellClipPlanes[key].constant = info.minY + info.height * 0.56;
      }
      if (state.shell === key) { applyCutawayVisibility(); applyMaterialState(); }
    });
  }
}

const DIM_GRAY = new THREE.Color(0x30333a);

/**
 * "Get close and the body goes see-through" — camera-distance-driven X-ray, active only
 * in Normal mode (X-ray/Cutaway/Focus already have their own explicit controls). Distance
 * is measured to the orbit target, which tracks the car/selected part, so it works whether
 * you're orbiting the whole car or zoomed onto one component.
 */
const PROXIMITY_NEAR = 220; // at/under this distance, shell is fully X-rayed
const PROXIMITY_FAR = 620;  // at/over this distance, shell is fully opaque — raised twice
                             // now so the fade is already visibly underway at the default
                             // reset view (~576 units out), not something you have to zoom
                             // a long way in to even notice starting

function proximityXrayStrength() {
  const dist = camera.position.distanceTo(controls.target);
  return THREE.MathUtils.clamp((PROXIMITY_FAR - dist) / (PROXIMITY_FAR - PROXIMITY_NEAR), 0, 1);
}

function applyMaterialState() {
  const sel = state.selectedId;
  const related = sel ? (PARTS.get(sel)?.related || []) : [];
  const faultPrimary = state.faultActive && activeFault ? activeFault.focus : [];
  const faultRelated = state.faultActive && activeFault ? activeFault.related : [];
  // A fault with no specific part (a system-only result from the server catalog) isolates by
  // SYSTEM instead — the same visual treatment as clicking that system in the nav — rather than
  // hiding every solid part, which is what a plain "faultActive" hide would otherwise do below.
  const faultHidesEverythingElse = state.faultActive && (faultPrimary.length > 0 || faultRelated.length > 0);
  const strength = state.mode === 'xray' ? state.xray : (state.mode === 'normal' ? proximityXrayStrength() : 0);
  const xrayLike = state.mode === 'xray' || (state.mode === 'normal' && strength > 0.02);

  for (const info of PARTS.values()) {
    const isFaultPrimary = faultPrimary.includes(info.id);
    const isFaultRelated = faultRelated.includes(info.id);
    const isSelected = !state.faultActive && info.id === sel;
    const isRelated = !state.faultActive && related.includes(info.id);
    const isExplicit = isSelected || isRelated || isFaultPrimary || isFaultRelated;
    const inFocusSystem = !state.focusSystem || info.system === state.focusSystem;

    // Opacity alone wasn't enough to make isolated-out parts disappear — metallic
    // materials still show a crisp specular glint at the edges even at a few percent
    // opacity, which is exactly the clutter that made "only this system" or "only the
    // faulty part" hard to read. Actually hiding them is reliable; the flag makes sure
    // this only restores visibility it turned off itself, not a part some other system
    // (shell/wheel toggling) already hides for an unrelated reason.
    const shouldHide = info.xrayRole === 'solid' && !isExplicit
      && (faultHidesEverythingElse || (state.focusSystem && !inFocusSystem));
    if (shouldHide) {
      if (info.mesh.visible) { info.mesh.visible = false; info._hiddenByFilter = true; }
    } else if (info._hiddenByFilter) {
      info.mesh.visible = true;
      info._hiddenByFilter = false;
    }

    eachMaterial(info, (mat) => {
      const base = mat.userData;
      if (!base || !base.baseColor) return;

      let opacity = base.baseOpacity;
      if (xrayLike) {
        if (info.xrayRole === 'shell') opacity = THREE.MathUtils.lerp(1, 0.1, strength);
        else if (info.xrayRole === 'glass') opacity = THREE.MathUtils.lerp(base.baseOpacity, 0.12, strength);
      }

      let dim = 1;
      // Faded to near-nothing (not just dimmed) — the whole point of the fault view is
      // "this one part is the problem," which got lost when everything else stayed
      // visible enough to clutter the picture.
      if (state.faultActive) dim = (isFaultPrimary || isFaultRelated) ? 1 : 0.04;
      else if (sel) dim = isSelected ? 1 : (isRelated ? 0.9 : 0.3);
      // Passive filters (system focus, diagnostic view) never override an explicit
      // selection/fault/related highlight — otherwise picking a part outside the active
      // filter would make it invisible instead of showing it, which defeats the point.
      if (!isExplicit) {
        if (!inFocusSystem) dim = 0; // "focus on a system" means isolate it, not just fade the rest
        if (state.diagView) {
          if (info.diagTier === 3) dim = Math.min(dim, 0.15);
          else if (info.diagTier === 2) dim = Math.min(dim, 0.55);
        }
      }

      const finalOpacity = Math.max(0.035, opacity * dim);
      mat.color.copy(base.baseColor).lerp(DIM_GRAY, 1 - dim);
      mat.opacity = finalOpacity;
      mat.transparent = finalOpacity < 0.995 || base.baseTransparent;
      mat.depthWrite = finalOpacity > 0.85;

      const xrayShellGlow = xrayLike && info.xrayRole === 'shell' && inFocusSystem;

      // Kept deliberately restrained — a professional engineering viewer, not a hologram.
      if (isFaultPrimary) { mat.emissive.setHex(0xff5c3c); mat.emissiveIntensity = pulse(2.4) * 0.8 + 0.3; }
      else if (isFaultRelated) { mat.emissive.setHex(0xf2b347); mat.emissiveIntensity = 0.5; }
      else if (isSelected) { mat.emissive.setHex(0x35c9e1); mat.emissiveIntensity = pulse(2.2) * 0.55 + 0.35; }
      else if (isRelated) { mat.emissive.setHex(0x35c9e1); mat.emissiveIntensity = 0.22; }
      else if (xrayShellGlow) { mat.emissive.setHex(0x1e4852); mat.emissiveIntensity = THREE.MathUtils.lerp(0.05, 0.4, strength); }
      else { mat.emissive.copy(base.baseEmissive); mat.emissiveIntensity = base.baseEmissiveIntensity; }
    });
  }

  if (state.shell !== 'procedural') {
    const inFocusSystem = !state.focusSystem || state.focusSystem === 'body';
    // Same reasoning as the per-part hide above: a system focus (or a fault) means the
    // whole car body should actually disappear, not just fade — it's a single mesh, so
    // there's no "was something else hiding it" case to worry about conflicting with.
    const shellVisible = !state.faultActive && inFocusSystem;
    shellGroups[state.shell].visible = shellVisible;
    if (shellVisible) {
      shellGroups[state.shell].traverse((o) => {
        if (!o.isMesh) return;
        const mats = Array.isArray(o.material) ? o.material : [o.material];
        mats.forEach((mat) => {
          const base = mat.userData;
          if (!base || !base.baseColor) return;
          let opacity = base.baseOpacity;
          if (xrayLike) opacity = THREE.MathUtils.lerp(1, 0.1, strength);
          const dim = state.diagView ? 0.15 : 1;
          const finalOpacity = Math.max(0.035, opacity * dim);
          mat.color.copy(base.baseColor).lerp(DIM_GRAY, 1 - dim);
          mat.opacity = finalOpacity;
          mat.transparent = finalOpacity < 0.995 || base.baseTransparent;
          mat.depthWrite = finalOpacity > 0.85;
          if (xrayLike) { mat.emissive.setHex(0x1e4852); mat.emissiveIntensity = THREE.MathUtils.lerp(0.05, 0.35, strength); }
          else { mat.emissive.copy(base.baseEmissive); mat.emissiveIntensity = base.baseEmissiveIntensity; }
        });
      });
    }
  }
}

function applyExplodePositions() {
  for (const info of PARTS.values()) {
    const base = info.fitPos || info.basePos;
    info.mesh.position.copy(base).addScaledVector(info.explodeOffset, state.explode);
    if (info.fitQuat) info.mesh.quaternion.copy(info.fitQuat);
  }
}

/**
 * Sourced shells (Sloyd, Quaternius) aren't the same height as the procedural body we
 * originally built the 90 mechanical parts against — measured directly (Sloyd's own
 * bounding box comes out ~127 units tall vs. our ~149 roofline). Without this, things
 * like the ignition coils visibly poke out above the real hood/roof. This compresses
 * each part's Y position toward a ground-level pivot by a measured ratio — geometry
 * itself is never scaled (so nothing gets visually squashed), only repositioned, and
 * X/Z stay untouched. Wheels and shell panels are excluded; they're already correct.
 */
const SHELL_FIT = {
  // body/suspension/transmission etc. — measured shell height ~127 vs our ~149
  procedural: { pivotY: 0, factor: 1, engineFactor: 1 },
  // the hood sits lower than the roofline, so engine-bay items (anything originally
  // forward of the firewall, basePos.z > 90) need a second, stronger compression —
  // otherwise the engine/intake visibly pokes out above the hood even once the rest fits.
  sloyd: { pivotY: 20, factor: 0.8, engineFactor: 0.45 },
  quaternius: { pivotY: 20, factor: 0.8, engineFactor: 0.45 },
};

// Brake/suspension geometry built at a wheel's exact z tracks that wheel's own position —
// which is deliberately never compressed, since a wheel must stay planted on the ground
// regardless of shell. Compressing disc/caliper/control-arm/etc. independently left them
// sagging below the (uncompressed) wheel they're actually bolted to.
const WHEEL_Z_DEPTHS = [...new Set(WHEELS.map((w) => Math.abs(w.z)))];

function compressY(y, z, fit) {
  if (WHEEL_Z_DEPTHS.includes(Math.abs(z))) return y;
  const factor = z > 90 ? fit.engineFactor : fit.factor;
  return fit.pivotY + (y - fit.pivotY) * factor;
}

function applyShellFit(key) {
  const fit = SHELL_FIT[key] || SHELL_FIT.procedural;
  for (const info of PARTS.values()) {
    if (info.id.startsWith('wheel_')) { info.fitPos = info.basePos; continue; }

    if (info.linkSource) {
      // A rigid two-point link (e.g. a tie rod): re-derive both endpoints under this
      // shell's compression independently, then rebuild position, orientation and length
      // from them — translating the pivot alone would leave it pointing at the wrong
      // angle once the two things it connects have each moved by a different amount.
      const { a, b, radius } = info.linkSource;
      const pa = new THREE.Vector3(a[0], compressY(a[1], a[2], fit), a[2]);
      const pb = new THREE.Vector3(b[0], compressY(b[1], b[2], fit), b[2]);
      const newLength = pa.distanceTo(pb);
      info.mesh.geometry.dispose();
      info.mesh.geometry = new THREE.CylinderGeometry(radius, radius, newLength, 8);
      info.fitPos = pa.clone().lerp(pb, 0.5);
      info.fitQuat = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), pb.clone().sub(pa).normalize());
      continue;
    }

    if (info.curveSource) {
      // tubes (hoses, harnesses, pipes) bake absolute points into their geometry —
      // rebuild it with each control point compressed by its own z, since one hose can
      // run from under the hood to well behind it.
      const { points, radius } = info.curveSource;
      const newPoints = points.map(([x, y, z]) => [x, compressY(y, z, fit), z]);
      const curve = new THREE.CatmullRomCurve3(newPoints.map((p) => new THREE.Vector3(...p)));
      const newGeo = new THREE.TubeGeometry(curve, Math.max(16, points.length * 8), radius, 8, false);
      info.mesh.geometry.dispose();
      info.mesh.geometry = newGeo;
      continue;
    }

    if (info.xrayRole !== 'solid') { info.fitPos = info.basePos; continue; }
    const y = compressY(info.basePos.y, info.basePos.z, fit);
    info.fitPos = new THREE.Vector3(info.basePos.x, y, info.basePos.z);
  }
}

/* ----------------------------------------------------------------------- */
/* Fault demo data (demonstration only — not real diagnostics)             */
/* ----------------------------------------------------------------------- */

const FAULTS = {
  p0301: {
    code: 'P0301', label: '1-ლი ცილინდრის აალების გაცდენა',
    focus: ['spark_plug_1', 'ignition_coil_1'],
    related: ['cylinder_head', 'engine_block', 'ecu', 'fuel_rail'],
    relatedList: ['ბობინა № 1 (Ignition Coil 1)', 'ცილინდრის თავი (Cylinder Head)', 'ECU (Engine Control Unit)', 'პისტონი № 1 (Piston 1)'],
    causesList: [
      'ამორტიზებული ან დაბინძურებული სანთელი', 'გაუმართავი ან დაზიანებული ბობინა', 'დაბალი შეკუმშვა ცილინდრში',
      'ვაკუუმის გაჟონვა', 'საწვავის მიწოდების პრობლემა № 1 ცილინდრზე', 'სადენის ან კონექტორის კონტაქტის პრობლემა',
    ],
  },
  p0171: {
    code: 'P0171', label: 'სისტემა ზედმეტად მჭლეა — ბანკი 1',
    focus: ['maf_sensor', 'o2_sensor_upstream'],
    related: ['intake_manifold', 'fuel_pump', 'fuel_rail', 'ecu'],
    relatedList: ['MAF სენსორი (ჰაერის მასის სენსორი)', 'ლამბდა ზონდი (წინა)', 'საშუშების კოლექტორი', 'საწვავის ტუმბო'],
    causesList: [
      'ვაკუუმის გაჟონვა საშუშების სისტემაში', 'დაბინძურებული ან გაუმართავი MAF სენსორი', 'სუსტი ან გაუმართავი საწვავის ტუმბო',
      'დაცმენილი საწვავის ფილტრი', 'გაუმართავი ლამბდა ზონდი', 'გამონაბოლქვის გაჟონვა ლამბდა ზონდამდე',
    ],
  },
  p0a80: {
    code: 'P0A80', label: 'საჭიროა ჰიბრიდული ბატარეის პაკეტის შეცვლა',
    focus: ['hybrid_battery_pack'],
    related: ['power_control_unit', 'hv_cable_main', 'drive_motor_generator'],
    relatedList: ['გამძლავრების მართვის ბლოკი (ინვერტორი)', 'მაღალი ძაბვის კაბელი', 'ელექტრო ძრავა-გენერატორი'],
    causesList: [
      'ბატარეის უჯრედებს შორის დაბალანსების პრობლემა', 'ბატარეის მოდულების ხანდაზმულობა/ცვეთა',
      'ბატარეის პაკეტის გაცივების სისტემის გაუმართაობა', 'შიდა იზოლაციის დაზიანება',
    ],
  },
  p0128: {
    code: 'P0128', label: 'გამაცივებელი სითხე ვერ აღწევს სამუშაო ტემპერატურას',
    focus: ['thermostat_housing', 'coolant_temp_sensor'],
    related: ['water_pump', 'radiator', 'ecu'],
    relatedList: ['წყლის ტუმბო', 'რადიატორი', 'ძრავის კომპიუტერი (ECU)'],
    causesList: [
      'თერმოსტატი ჩარჩენილია ღია მდგომარეობაში', 'გამაცივებლის დაბალი დონე სისტემაში',
      'გაუმართავი გამაცივებლის ტემპერატურის სენსორი', 'ხანგრძლივი უქმი სვლა ცივ ამინდში',
    ],
  },
  p0172: {
    code: 'P0172', label: 'სისტემა ზედმეტად მდიდარია — ბანკი 1',
    focus: ['maf_sensor', 'o2_sensor_upstream'],
    related: ['fuel_pump', 'fuel_rail', 'ecu'],
    relatedList: ['MAF სენსორი', 'ლამბდა ზონდი (წინა)', 'საწვავის რეიკა'],
    causesList: [
      'გაუმართავი ან დაბინძურებული MAF სენსორი', 'საწვავის წნევის მარეგულირებლის გაუმართაობა',
      'გაჟონვადი ინჟექტორი', 'დაბინძურებული ჰაერის ფილტრი', 'გაუმართავი ლამბდა ზონდი',
    ],
  },
  p0300: {
    code: 'P0300', label: 'შემთხვევითი/მრავალცილინდრიანი აალების გაცდენა',
    focus: ['spark_plug_1', 'spark_plug_2', 'spark_plug_3', 'spark_plug_4'],
    related: ['ecu', 'fuel_rail', 'cylinder_head'],
    relatedList: ['ძრავის კომპიუტერი (ECU)', 'საწვავის რეიკა', 'ცილინდრის თავი'],
    causesList: [
      'ზოგადი ვაკუუმის გაჟონვა', 'დაბალი საწვავის წნევა', 'რამდენიმე ცვეთილი სანთელი ერთდროულად',
      'დაბინძურებული საწვავი', 'დაბალი შეკუმშვა რამდენიმე ცილინდრში',
    ],
  },
  p0420: {
    code: 'P0420', label: 'კატალიზატორის ეფექტურობა ზღვარს ქვემოთაა (ბანკი 1)',
    focus: ['catalytic_converter'],
    related: ['o2_sensor_upstream', 'o2_sensor_downstream'],
    relatedList: ['ლამბდა ზონდი (წინა)', 'ლამბდა ზონდი (უკანა)'],
    causesList: ['ცვეთილი/დაზიანებული კატალიზატორი', 'ძრავის ხანგრძლივი გაუმართავი მუშაობა', 'გაუმართავი ლამბდა ზონდი იძლევა ცრუ წაკითხვას'],
  },
  p0562: {
    code: 'P0562', label: 'სისტემის ძაბვა დაბალია',
    focus: ['battery'],
    related: ['alternator', 'fuse_box'],
    relatedList: ['გენერატორი', 'საცვენი ბლოკი'],
    causesList: ['დატენვილი/ცვეთილი აკუმულატორი', 'სუსტი შეერთება კლემებზე', 'გაუმართავი გენერატორი'],
  },
  p0563: {
    code: 'P0563', label: 'სისტემის ძაბვა მაღალია',
    focus: ['alternator'],
    related: ['battery', 'fuse_box'],
    relatedList: ['აკუმულატორი', 'საცვენი ბლოკი'],
    causesList: ['გენერატორის მარეგულირებლის გაუმართაობა', 'ცუდი მიწის კონტაქტი'],
  },
  p0601: {
    code: 'P0601', label: 'ECU-ს შიდა მეხსიერების შეცდომა',
    focus: ['ecu'],
    related: ['battery', 'fuse_box'],
    relatedList: ['აკუმულატორი', 'საცვენი ბლოკი'],
    causesList: ['ძაბვის მოულოდნელი ვარდნა განახლების დროს', 'თავად ბლოკის შიდა გაუმართაობა'],
  },
  p0700: {
    code: 'P0700', label: 'ტრანსმისიის მართვის სისტემის გაუმართაობა',
    focus: ['tcm'],
    related: ['transmission_housing', 'ecu'],
    relatedList: ['გადაცემათა კოლოფი', 'ძრავის კომპიუტერი (ECU)'],
    causesList: ['TCM-ში შენახული კოდი, საჭიროებს დეტალურ წაკითხვას TCM-დან', 'დაზიანებული სადენი TCM-სა და ECU-ს შორის'],
  },
  p0aa6: {
    code: 'P0AA6', label: 'ჰიბრიდული მაღალვოლტაჟიანი სისტემის იზოლაციის დაზიანება',
    focus: ['hv_cable_main'],
    related: ['hybrid_battery_pack', 'power_control_unit'],
    relatedList: ['ჰიბრიდული ბატარეის პაკეტი', 'გამძლავრების მართვის ბლოკი (ინვერტორი)'],
    causesList: ['დაზიანებული კაბელის იზოლაცია', 'ტენიანობა მაღალვოლტაჟიან კონექტორში', 'დაზიანებული HV კომპონენტის კორპუსი'],
  },
  u0100: {
    code: 'U0100', label: 'კავშირი ECU-სთან დაკარგულია',
    focus: ['ecu'],
    related: ['battery', 'fuse_box', 'wiring_harness_main'],
    relatedList: ['აკუმულატორი', 'საცვენი ბლოკი', 'მთავარი საკაბელო კონა'],
    causesList: ['გადაწვეტილი ან დაჟანგული კავშირი', 'საცვენის ამოვარდნა', 'ECU-ს კვების პრობლემა'],
  },
};

/* Per-cylinder misfire (P0301-304) and injector-circuit (P0201-204) codes follow the exact
 * same pattern four times — generated instead of hand-duplicated. */
const CYL_ORDINAL_KA = ['1-ლი', '2-ე', '3-ე', '4-ე'];
for (let i = 1; i <= 4; i++) {
  if (i > 1) {
    FAULTS[`p030${i}`] = {
      code: `P030${i}`, label: `${CYL_ORDINAL_KA[i - 1]} ცილინდრის აალების გაცდენა`,
      focus: [`spark_plug_${i}`, `ignition_coil_${i}`],
      related: ['cylinder_head', 'engine_block', 'ecu', 'fuel_rail'],
      relatedList: [`ბობინა № ${i}`, 'ცილინდრის თავი', 'ძრავის კომპიუტერი (ECU)', 'საწვავის რეიკა'],
      causesList: [
        'ამორტიზებული ან დაბინძურებული სანთელი', 'გაუმართავი ან დაზიანებული ბობინა', 'დაბალი შეკუმშვა ცილინდრში',
        'ვაკუუმის გაჟონვა', `საწვავის მიწოდების პრობლემა № ${i} ცილინდრზე`, 'სადენის ან კონექტორის კონტაქტის პრობლემა',
      ],
    };
  }
  FAULTS[`p020${i}`] = {
    code: `P020${i}`, label: `${i}-ე ცილინდრის ინჟექტორის წრედის გაუმართაობა`,
    focus: [`injector_${i}`],
    related: ['fuel_rail', 'ecu'],
    relatedList: ['საწვავის რეიკა', 'ძრავის კომპიუტერი (ECU)'],
    causesList: ['ინჟექტორის გრაგნილის გაწყვეტა', 'დაზიანებული სადენი ან კონექტორი', 'ECU-ს მართვის წრედის გაუმართაობა'],
  };
}

/* Most single-sensor circuits generate a whole numbered block under SAE J2012, not one
 * code — Circuit Malfunction / Range-Performance / Low Input / High Input / Intermittent.
 * P0131 (reported missing) turned out to be exactly this: a sibling of P0130 on the same
 * sensor. Generated here per family instead of hand-written 5-6x per sensor. */
function addCircuitFamily(baseCode, focusId, nameKa, related, relatedList) {
  const n = parseInt(baseCode.slice(2), 10);
  const variants = [
    { offset: 0, label: `${nameKa} — წრედის გაუმართაობა`, causes: ['დაზიანებული სადენი ან კონექტორი', 'თავად სენსორის გაუმართაობა', 'დაჟანგული კონტაქტები'] },
    { offset: 1, label: `${nameKa} — დიაპაზონის/ხარისხის პრობლემა`, causes: ['სენსორის ჩვენება მოსალოდნელ დიაპაზონს გარეთაა', 'დაკავშირებული სისტემის გაჟონვა (მაგ. ვაკუუმი)', 'სენსორის ხანდაზმულობით გამოწვეული დრეიფი'] },
    { offset: 2, label: `${nameKa} — დაბალი სიგნალი`, causes: ['მოკლე ჩართვა მასაზე', 'გაწყვეტილი წრედი', 'სენსორი ჩარჩენილია დაბალ მნიშვნელობაზე'] },
    { offset: 3, label: `${nameKa} — მაღალი სიგნალი`, causes: ['მოკლე ჩართვა კვებაზე', 'გაწყვეტილი მასის სადენი', 'სენსორი ჩარჩენილია მაღალ მნიშვნელობაზე'] },
    { offset: 4, label: `${nameKa} — წყვეტადი სიგნალი`, causes: ['მოშვებული ან დაჟანგული კონექტორი', 'დაზიანებული იზოლაცია სადენზე', 'სუსტი მასის კონტაქტი'] },
  ];
  variants.forEach(({ offset, label, causes }) => {
    const code = `P0${n + offset}`;
    FAULTS[`p0${n + offset}`] = { code, label, focus: [focusId], related, relatedList, causesList: causes };
  });
}
addCircuitFamily('P0100', 'maf_sensor', 'MAF სენსორი', ['air_filter', 'throttle_body', 'ecu'], ['ჰაერის ფილტრი', 'დროსელის სხეული', 'ძრავის კომპიუტერი (ECU)']);
addCircuitFamily('P0105', 'map_sensor', 'MAP სენსორი', ['intake_manifold', 'ecu'], ['საშუშების კოლექტორი', 'ძრავის კომპიუტერი (ECU)']);
addCircuitFamily('P0115', 'coolant_temp_sensor', 'გამაცივ. ტემპერატურის სენსორი', ['thermostat_housing', 'ecu'], ['თერმოსტატი', 'ძრავის კომპიუტერი (ECU)']);
addCircuitFamily('P0120', 'throttle_body', 'დროსელის პოზიციის სენსორი', ['intake_manifold', 'ecu'], ['საშუშების კოლექტორი', 'ძრავის კომპიუტერი (ECU)']);
addCircuitFamily('P0335', 'crank_position_sensor', 'მუხლის ლილვის სენსორი', ['engine_block', 'ecu'], ['ძრავის ბლოკი', 'ძრავის კომპიუტერი (ECU)']);
addCircuitFamily('P0340', 'cam_position_sensor', 'ბარგის ლილვის სენსორი', ['cylinder_head', 'ecu'], ['ცილინდრის თავი', 'ძრავის კომპიუტერი (ECU)']);

/* O2/lambda sensors follow their own 6-code SAE block (Malfunction / Low Voltage / High
 * Voltage / Slow Response / No Activity / Heater Circuit) instead of the generic 5 above. */
function addO2Family(baseCode, focusId, nameKa, related, relatedList) {
  const n = parseInt(baseCode.slice(2), 10);
  const variants = [
    { offset: 0, label: `${nameKa} — წრედის გაუმართაობა`, causes: ['დაზიანებული სადენი ან კონექტორი', 'თავად ზონდის გაუმართაობა'] },
    { offset: 1, label: `${nameKa} — დაბალი ძაბვა`, causes: ['გამონაბოლქვის გაჟონვა ზონდამდე', 'დაბერებული ზონდი', 'საწვავის ნარევის ზედმეტად მჭლე ყოფნა'] },
    { offset: 2, label: `${nameKa} — მაღალი ძაბვა`, causes: ['საწვავის ნარევის ზედმეტად მდიდარი ყოფნა', 'დაზიანებული ზონდი', 'ჟანგბადის სენსორის დაბინძურება'] },
    { offset: 3, label: `${nameKa} — ნელი რეაგირება`, causes: ['დაბერებული/ცვეთილი ზონდი', 'ზონდის დაბინძურება (მაგ. სილიკონით)'] },
    { offset: 4, label: `${nameKa} — აქტივობა არ ფიქსირდება`, causes: ['ზონდის წრედის გაწყვეტა', 'სრულად გაუმართავი ზონდი'] },
    { offset: 5, label: `${nameKa} — გამათბობელი წრედის გაუმართაობა`, causes: ['დაზიანებული გამათბობელი ელემენტი', 'გამათბობლის ზედმეტი დენის დაცვის ჩართვა', 'დაზიანებული სადენი'] },
  ];
  variants.forEach(({ offset, label, causes }) => {
    const code = `P0${n + offset}`;
    FAULTS[`p0${n + offset}`] = { code, label, focus: [focusId], related, relatedList, causesList: causes };
  });
}
addO2Family('P0130', 'o2_sensor_upstream', 'ლამბდა ზონდი (წინა)', ['exhaust_manifold', 'catalytic_converter', 'ecu'], ['საშუშების კოლექტორი (გამონაბოლქვი)', 'კატალიზატორი', 'ძრავის კომპიუტერი (ECU)']);
addO2Family('P0136', 'o2_sensor_downstream', 'ლამბდა ზონდი (უკანა)', ['catalytic_converter', 'ecu'], ['კატალიზატორი', 'ძრავის კომპიუტერი (ECU)']);

FAULTS.p0230 = {
  code: 'P0230', label: 'საწვავის ტუმბოს ძირითადი წრედის გაუმართაობა',
  focus: ['fuel_pump'], related: ['fuel_tank', 'fuel_rail', 'ecu'],
  relatedList: ['საწვავის ავზი', 'საწვავის რეიკა', 'ძრავის კომპიუტერი (ECU)'],
  causesList: ['დაზიანებული სარელეო წრედი', 'ცვეთილი ტუმბო', 'დაჟანგული კონექტორი'],
};
FAULTS.p0231 = {
  code: 'P0231', label: 'საწვავის ტუმბოს დამხმარე წრედი — დაბალი სიგნალი',
  focus: ['fuel_pump'], related: ['fuel_tank', 'ecu'],
  relatedList: ['საწვავის ავზი', 'ძრავის კომპიუტერი (ECU)'],
  causesList: ['მოკლე ჩართვა მასაზე', 'გაწყვეტილი წრედი'],
};
FAULTS.p0232 = {
  code: 'P0232', label: 'საწვავის ტუმბოს დამხმარე წრედი — მაღალი სიგნალი',
  focus: ['fuel_pump'], related: ['fuel_tank', 'ecu'],
  relatedList: ['საწვავის ავზი', 'ძრავის კომპიუტერი (ECU)'],
  causesList: ['მოკლე ჩართვა კვებაზე', 'დაზიანებული სარელეო წრედი'],
};

/** Holds the active fault's full display data (not just a FAULTS key) so the same rendering
 *  and highlighting path works whether the code came from the small local, hand-curated table
 *  (an exact 3D part — `focus`/`related` are part ids) or from the real server-side DTC catalog
 *  (thousands of codes, but it only knows a general SYSTEM, not this model's part ids — see
 *  `focusSystem`). */
let activeFault = null;

function activateFault(data) {
  const previous = activeFault;
  activeFault = data || null;
  state.faultActive = !!activeFault;
  state.selectedId = null;

  const hasSpecificParts = !!(activeFault && (activeFault.focus.length || activeFault.related.length));
  if (activeFault && !hasSpecificParts && activeFault.focusSystem) {
    // No exact part to point at — isolate the whole system instead, same as clicking it in the nav.
    setFocusSystem(activeFault.focusSystem);
  } else if (!activeFault && previous && !(previous.focus.length || previous.related.length) && previous.focusSystem) {
    // Clearing a system-only fault should also drop the system filter it turned on.
    setFocusSystem(null);
  }

  document.getElementById('fault-code-feedback').textContent = '';
  applyMaterialState();
  updateInfoPanel();
  if (activeFault && activeFault.focus[0]) focusOnPart(activeFault.focus[0], { tight: true });
}

/** SAE J2012 P-code numeric sub-ranges are standard and manufacturer-independent for the
 *  generic codes this model-less lookup is limited to — good enough to pick a SYSTEM (never a
 *  specific part) without inventing per-code data the catalog doesn't have. B/C/U fall back to
 *  the catalog's own `category`, which is always populated. */
function mapToSystem(code, category) {
  const letter = code[0];
  if (letter === 'P') {
    const n = parseInt(code.slice(1, 4), 10);
    if (Number.isFinite(n)) {
      if (n <= 299) return 'fuel';          // P00xx-P02xx: fuel & air metering, injector circuits
      if (n <= 399) return 'engine';        // P03xx: ignition system / misfire
      if (n <= 499) return 'exhaust';       // P04xx: EGR / EVAP / catalyst / secondary air
      if (n <= 599) return 'engine';        // P05xx: vehicle speed, idle, auxiliary inputs
      if (n <= 699) return 'electrical';    // P06xx: computer and auxiliary outputs
      if (n <= 899) return 'transmission';  // P07xx-P08xx
    }
    return 'engine';
  }
  if (letter === 'B' || category === 'body') return 'body';
  if (letter === 'C' || category === 'chassis') return 'suspension';
  if (letter === 'U' || category === 'network') return 'electrical';
  if (category === 'powertrain') return 'engine';
  return null;
}

/** Typed in directly, or arriving via a "?code=" link from the app's own scan results.
 *  First tries the small local, hand-curated table (exact 3D part, hand-written causes); for
 *  anything else it asks the real OBD360 DTC catalog on the server — thousands of real SAE
 *  codes, the same data the app itself uses — instead of inventing an answer. */
async function lookupFaultCode(raw) {
  const feedback = document.getElementById('fault-code-feedback');
  const normalized = raw.trim().toUpperCase().replace(/\s+/g, '');
  // An empty search clears whatever fault is active — removing the dropdown took away the
  // only other way to exit the fault view without clicking directly on a 3D part.
  if (!normalized) { feedback.textContent = ''; if (activeFault) activateFault(null); return; }
  const withPrefix = /^[A-Z]/.test(normalized) ? normalized : `P${normalized}`;

  const localKey = Object.keys(FAULTS).find((k) => FAULTS[k].code.toUpperCase() === withPrefix);
  if (localKey) {
    const f = FAULTS[localKey];
    activateFault({
      code: f.code, label: f.label, focus: f.focus, related: f.related,
      causesList: f.causesList, relatedTitle: 'დაკავშირებული ნაწილები', relatedList: f.relatedList,
      focusSystem: null, caution: null,
    });
    return;
  }

  feedback.textContent = 'იძებნება…';
  try {
    const res = await fetch(`/api/public/dtc/${encodeURIComponent(withPrefix)}`);
    if (!res.ok) {
      feedback.textContent = res.status === 404
        ? `კოდი „${raw.trim()}" ვერ მოიძებნა ჩვენს ბაზაში.`
        : 'სერვერთან კავშირმა ვერ იმუშავა — სცადეთ თავიდან.';
      return;
    }
    const data = await res.json();
    activateFault({
      code: data.code, label: data.titleKa || data.code, focus: [], related: [],
      causesList: data.causesKa || [], relatedTitle: 'სიმპტომები', relatedList: data.symptomsKa || [],
      focusSystem: mapToSystem(data.code, data.category),
      caution: data.descriptionKa || null,
    });
  } catch {
    feedback.textContent = 'კავშირის შეცდომა — სცადეთ თავიდან.';
  }
}

document.getElementById('fault-code-btn').addEventListener('click', () => {
  lookupFaultCode(document.getElementById('fault-code-input').value);
});
document.getElementById('fault-code-input').addEventListener('keydown', (e) => {
  if (e.key === 'Enter') lookupFaultCode(e.target.value);
});

/* ----------------------------------------------------------------------- */
/* Selection, info panel                                                    */
/* ----------------------------------------------------------------------- */

const DIAG_TIER_LABELS = { 1: 'OBD2-ით პირდაპირ იკითხება', 2: 'OBD2-ით ირიბულად ჩანს', 3: 'ფიზიკური ნაწილი — OBD2 ვერ რეაგირებს' };

const infoEmpty = document.getElementById('info-empty');
const infoCard = document.getElementById('info-card');
const faultCard = document.getElementById('fault-card');

function selectPart(id) {
  state.faultActive = false;
  activeFault = null;
  state.selectedId = id;
  state.relatedIds = id ? (PARTS.get(id)?.related || []) : [];
  applyMaterialState();
  updateInfoPanel();
  if (id) focusOnPart(id);
}

function updateInfoPanel() {
  if (state.faultActive && activeFault) {
    infoEmpty.hidden = true; infoCard.hidden = true; faultCard.hidden = false;
    document.getElementById('fault-chip').textContent = activeFault.code;
    document.getElementById('fault-title').textContent = activeFault.label;
    document.getElementById('fault-related-title').textContent = activeFault.relatedTitle || 'დაკავშირებული ნაწილები';
    document.getElementById('fault-related').innerHTML = (activeFault.relatedList || []).map((t) => `<li>${t}</li>`).join('');
    document.getElementById('fault-causes').innerHTML = (activeFault.causesList || []).map((t) => `<li>${t}</li>`).join('');
    document.getElementById('fault-caution').textContent = activeFault.caution
      || 'კოდი მხოლოდ გაცდენას აღნიშნავს — ის არ ადასტურებს კონკრეტულად რომელი ნაწილია გაუმართავი.';
    return;
  }
  faultCard.hidden = true;
  const info = state.selectedId ? PARTS.get(state.selectedId) : null;
  if (!info) { infoEmpty.hidden = false; infoCard.hidden = true; return; }
  infoEmpty.hidden = true; infoCard.hidden = false;

  document.getElementById('info-system-chip').textContent = SYSTEMS[info.system]?.ka || info.system;
  const diagChip = document.getElementById('info-diag-chip');
  diagChip.className = `chip diag-chip diag-tier${info.diagTier}`;
  diagChip.textContent = DIAG_TIER_LABELS[info.diagTier] || '';
  document.getElementById('info-name-ka').textContent = info.nameKa;
  document.getElementById('info-name-en').textContent = info.nameEn;
  document.getElementById('info-function').textContent = info.fn;
  document.getElementById('info-location').textContent = info.loc;

  const relatedWrap = document.getElementById('info-related');
  relatedWrap.innerHTML = '';
  (info.related || []).forEach((relId) => {
    const relInfo = PARTS.get(relId);
    if (!relInfo) return;
    const chip = document.createElement('button');
    chip.className = 'related-chip';
    chip.type = 'button';
    chip.textContent = relInfo.nameKa;
    chip.addEventListener('click', () => selectPart(relId));
    relatedWrap.appendChild(chip);
  });
}

/* ----------------------------------------------------------------------- */
/* Picking                                                                  */
/* ----------------------------------------------------------------------- */

const raycaster = new THREE.Raycaster();
const ndc = new THREE.Vector2();
let downPos = null, downTime = 0;

renderer.domElement.addEventListener('pointerdown', (e) => { downPos = { x: e.clientX, y: e.clientY }; downTime = performance.now(); });
renderer.domElement.addEventListener('pointerup', (e) => {
  if (!downPos) return;
  const dist = Math.hypot(e.clientX - downPos.x, e.clientY - downPos.y);
  const dt = performance.now() - downTime;
  downPos = null;
  if (dist < 6 && dt < 500) handlePick(e);
});

function handlePick(e) {
  const rect = renderer.domElement.getBoundingClientRect();
  ndc.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
  ndc.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
  raycaster.setFromCamera(ndc, camera);
  const hits = raycaster.intersectObjects(carGroup.children, true).filter((h) => isVisible(h.object));
  if (hits.length === 0) { selectPart(null); return; }
  const id = hits[0].object.userData.partId;
  selectPart(id || null);
}

/* ----------------------------------------------------------------------- */
/* Camera presets & tweened focus                                          */
/* ----------------------------------------------------------------------- */

const PRESETS = {
  reset: { pos: [360, 220, 420], target: [0, 60, 0] },
  front: { pos: [0, 110, 430], target: [0, 70, 100] },
  rear: { pos: [0, 110, -430], target: [0, 70, -110] },
  left: { pos: [-430, 130, 0], target: [-20, 70, 0] },
  right: { pos: [430, 130, 0], target: [20, 70, 0] },
  top: { pos: [0, 620, 10], target: [0, 40, 10] },
  under: { pos: [0, -320, 280], target: [0, 20, 0] },
  engine: { pos: [150, 165, 235], target: [0, 90, 130] },
};

function tweenCamera(toPos, toTarget, duration = 700) {
  const fromPos = camera.position.clone();
  const fromTarget = controls.target.clone();
  const toP = new THREE.Vector3(...toPos);
  const toT = new THREE.Vector3(...toTarget);
  const start = performance.now();
  function step(now) {
    const t = Math.min(1, (now - start) / duration);
    const e = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
    camera.position.lerpVectors(fromPos, toP, e);
    controls.target.lerpVectors(fromTarget, toT, e);
    controls.update();
    if (t < 1) requestAnimationFrame(step);
  }
  requestAnimationFrame(step);
}

function focusOnPart(id, opts = {}) {
  const info = PARTS.get(id);
  if (!info) return;
  const targetPos = new THREE.Vector3();
  info.mesh.getWorldPosition(targetPos);
  const offset = camera.position.clone().sub(controls.target);
  // A fault focus needs a dependably close, consistent shot regardless of whatever angle
  // the camera happened to be left at — a small sensor lost in a wide shot was exactly
  // the "can't tell what it is" problem being fixed here.
  const [near, far] = opts.tight ? [70, 130] : [90, 230];
  const dist = Math.max(near, Math.min(offset.length(), far));
  offset.normalize().multiplyScalar(dist);
  tweenCamera([targetPos.x + offset.x, targetPos.y + offset.y, targetPos.z + offset.z], [targetPos.x, targetPos.y, targetPos.z], 650);
}

document.querySelectorAll('.cam-presets button').forEach((btn) => {
  btn.addEventListener('click', () => {
    const p = PRESETS[btn.dataset.cam];
    if (p) tweenCamera(p.pos, p.target, 800);
  });
});

/* ----------------------------------------------------------------------- */
/* Left panel: modes, system nav, fault demo                               */
/* ----------------------------------------------------------------------- */

const modeButtons = document.querySelectorAll('.mode-btn');
modeButtons.forEach((btn) => {
  btn.addEventListener('click', () => {
    const m = btn.dataset.mode;
    if (m === 'exploded') {
      const goingTo = state.explode < 0.5 ? 1 : 0;
      tweenExplode(goingTo);
      return;
    }
    state.mode = m;
    modeButtons.forEach((b) => b.classList.toggle('active', b.dataset.mode === m));
    applyCutawayVisibility();
    applyMaterialState();
  });
});

function tweenExplode(to) {
  const from = state.explode;
  const start = performance.now();
  const duration = 900;
  function step(now) {
    const t = Math.min(1, (now - start) / duration);
    state.explode = from + (to - from) * t;
    document.getElementById('explode-slider').value = state.explode;
    applyExplodePositions();
    if (t < 1) requestAnimationFrame(step);
  }
  requestAnimationFrame(step);
}

/**
 * A system filter, not a mode: independent of normal/xray/cutaway, so "focus on cooling"
 * and "x-ray" can be active together. Replaces the old 5th "focus" mode plus its
 * duplicate system list that only appeared while that mode was active.
 */
function setFocusSystem(key) {
  state.focusSystem = key;
  document.querySelectorAll('#system-nav .system-btn').forEach((b) => b.classList.toggle('active', (b.dataset.system || null) === key));
  applyMaterialState();
}

function buildSystemButtons(container) {
  container.innerHTML = '';
  const allBtn = document.createElement('button');
  allBtn.className = 'system-btn active';
  allBtn.type = 'button';
  allBtn.textContent = 'ყველა სისტემა';
  allBtn.addEventListener('click', () => setFocusSystem(null));
  container.appendChild(allBtn);

  Object.entries(SYSTEMS).forEach(([key, meta]) => {
    const btn = document.createElement('button');
    btn.className = 'system-btn';
    btn.type = 'button';
    btn.textContent = meta.ka;
    btn.dataset.system = key;
    btn.addEventListener('click', () => setFocusSystem(state.focusSystem === key ? null : key));
    container.appendChild(btn);
  });
}
buildSystemButtons(document.getElementById('system-nav'));


document.getElementById('xray-slider').addEventListener('input', (e) => {
  state.xray = parseFloat(e.target.value);
  applyMaterialState();
});
document.getElementById('explode-slider').addEventListener('input', (e) => {
  state.explode = parseFloat(e.target.value);
  applyExplodePositions();
});
document.getElementById('labels-toggle').addEventListener('change', (e) => {
  state.labelsOn = e.target.checked;
});
document.getElementById('diag-view-toggle').addEventListener('change', (e) => {
  state.diagView = e.target.checked;
  applyMaterialState();
});

/* ----------------------------------------------------------------------- */
/* Labels & leader line                                                     */
/* ----------------------------------------------------------------------- */

const labelEls = new Map();

/** Fewer labels when looking at the whole car, more once you zoom in to inspect a
 *  specific area — showing all ~20 diagnostic-tier labels at once from a wide shot is
 *  just noise; this keeps the label layer legible at any distance instead. */
function labelBudget() {
  const dist = camera.position.distanceTo(controls.target);
  return Math.round(THREE.MathUtils.clamp(THREE.MathUtils.mapLinear(dist, 150, 500, 20, 9), 9, 20));
}

function updateLabels() {
  if (!state.labelsOn) { labelLayer.style.display = 'none'; return; }
  labelLayer.style.display = '';
  leaderSvg.innerHTML = '';

  const rect = stage.getBoundingClientRect();
  const candidates = [];
  for (const info of PARTS.values()) {
    if (info.labelImportance <= 0 || !isVisible(info.mesh)) continue;
    // A system filter isolates that system — a label for a part that's been faded to
    // near-zero opacity would just be clutter pointing at nothing visible.
    const isIsolatedOut = state.focusSystem && info.system !== state.focusSystem
      && info.id !== state.selectedId && !state.relatedIds.includes(info.id);
    if (isIsolatedOut && !state.faultActive) continue;
    const wp = new THREE.Vector3();
    info.mesh.getWorldPosition(wp);
    const proj = wp.clone().project(camera);
    if (proj.z > 1 || proj.z < -1) continue;
    const sx = (proj.x * 0.5 + 0.5) * rect.width;
    const sy = (1 - (proj.y * 0.5 + 0.5)) * rect.height;
    if (sx < 0 || sx > rect.width || sy < 0 || sy > rect.height) continue;
    const dist = wp.distanceTo(camera.position);
    const boost = info.id === state.selectedId ? 4 : (state.relatedIds.includes(info.id) ? 1.8 : 1);
    candidates.push({ info, sx, sy, trueX: sx, trueY: sy, dist, score: info.labelImportance * boost });
  }
  candidates.sort((a, b) => b.score - a.score || a.dist - b.dist);
  const chosen = candidates.slice(0, labelBudget()).sort((a, b) => a.sy - b.sy);

  const minGap = 15;
  for (let i = 1; i < chosen.length; i++) {
    if (chosen[i].sy - chosen[i - 1].sy < minGap) chosen[i].sy = chosen[i - 1].sy + minGap;
  }
  // A run of 3+ labels stacked into one column by the nudge above is unreadable —
  // several of the new diagnostic sensors sit close together in the engine bay, so this
  // happens often. Fan a run out left/right instead; each one keeps a thin leader line
  // back to its real position below, so it's still obvious which label is which.
  let runStart = 0;
  for (let i = 1; i <= chosen.length; i++) {
    const broke = i === chosen.length || chosen[i].sy - chosen[i - 1].sy > minGap + 0.5;
    if (broke) {
      const runLen = i - runStart;
      if (runLen >= 3) {
        for (let j = runStart; j < i; j++) {
          const k = j - runStart;
          chosen[j].sx += (k % 2 === 0 ? 1 : -1) * (30 + Math.floor(k / 2) * 14);
        }
      }
      runStart = i;
    }
  }

  const used = new Set();
  chosen.forEach(({ info, sx, sy, trueX, trueY }) => {
    used.add(info.id);
    let el = labelEls.get(info.id);
    if (!el) { el = document.createElement('div'); el.className = 'part-label'; labelLayer.appendChild(el); labelEls.set(info.id, el); }
    el.textContent = info.nameKa;
    el.className = `part-label label-tier${info.diagTier}`;
    el.style.left = `${sx}px`;
    el.style.top = `${sy}px`;
    el.classList.toggle('selected', info.id === state.selectedId);

    const moved = Math.hypot(sx - trueX, sy - trueY);
    if (moved > 8 && info.id !== state.selectedId) {
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', trueX); line.setAttribute('y1', trueY);
      line.setAttribute('x2', sx); line.setAttribute('y2', sy);
      line.setAttribute('stroke', 'rgba(255,255,255,0.22)'); line.setAttribute('stroke-width', '1');
      leaderSvg.appendChild(line);
    }
  });
  for (const [id, el] of labelEls) if (!used.has(id)) el.classList.add('hidden-label');

  if (state.selectedId) {
    const info = PARTS.get(state.selectedId);
    const entry = chosen.find((c) => c.info.id === state.selectedId);
    if (info && entry) {
      const wp = new THREE.Vector3();
      info.mesh.getWorldPosition(wp);
      const proj = wp.clone().project(camera);
      const ax = (proj.x * 0.5 + 0.5) * rect.width;
      const ay = (1 - (proj.y * 0.5 + 0.5)) * rect.height;
      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', ax); line.setAttribute('y1', ay);
      line.setAttribute('x2', entry.sx); line.setAttribute('y2', entry.sy);
      line.setAttribute('stroke', '#35C9E1'); line.setAttribute('stroke-width', '1.2'); line.setAttribute('opacity', '0.85');
      leaderSvg.appendChild(line);
      const dot = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
      dot.setAttribute('cx', ax); dot.setAttribute('cy', ay); dot.setAttribute('r', '3.2'); dot.setAttribute('fill', '#35C9E1');
      leaderSvg.appendChild(dot);
    }
  }
}

/* ----------------------------------------------------------------------- */
/* Resize                                                                   */
/* ----------------------------------------------------------------------- */

function onResize() {
  const rect = stage.getBoundingClientRect();
  renderer.setSize(rect.width, rect.height, false);
  camera.aspect = rect.width / Math.max(1, rect.height);
  camera.updateProjectionMatrix();
  leaderSvg.setAttribute('width', rect.width);
  leaderSvg.setAttribute('height', rect.height);
}
window.addEventListener('resize', onResize);
onResize();

/* ----------------------------------------------------------------------- */
/* Collapsible side/bottom panels — so the car can be viewed full-screen   */
/* without losing the controls, which just re-expand the same way.        */
/* ----------------------------------------------------------------------- */

function setupPanelToggles() {
  const panelLeft = document.getElementById('panel-left');
  const panelRight = document.getElementById('panel-right');
  const bottombar = document.getElementById('bottombar');
  const toggleLeft = document.getElementById('toggle-left');
  const toggleRight = document.getElementById('toggle-right');
  const toggleBottom = document.getElementById('toggle-bottom');

  toggleLeft.addEventListener('click', () => {
    const collapsed = panelLeft.classList.toggle('collapsed');
    toggleLeft.textContent = collapsed ? '›' : '‹';
    onResize();
  });
  toggleRight.addEventListener('click', () => {
    const collapsed = panelRight.classList.toggle('collapsed');
    toggleRight.textContent = collapsed ? '‹' : '›';
    onResize();
  });
  toggleBottom.addEventListener('click', () => {
    const collapsed = bottombar.classList.toggle('collapsed');
    toggleBottom.textContent = collapsed ? '⌃' : '⌄';
    onResize();
  });
}
setupPanelToggles();

/* ----------------------------------------------------------------------- */
/* Init + animation loop                                                    */
/* ----------------------------------------------------------------------- */

setShell('sloyd');

applyCutawayVisibility();
applyExplodePositions();
applyMaterialState();
updateInfoPanel();
modeButtons.forEach((b) => b.classList.toggle('active', b.dataset.mode === 'normal'));

// Arriving from the app's own scan results via "?code=P0171": open straight to that fault,
// same as if the visitor had typed it in themselves.
const linkedCode = new URLSearchParams(location.search).get('code');
if (linkedCode) {
  document.getElementById('fault-code-input').value = linkedCode;
  lookupFaultCode(linkedCode);
}

let firstFrame = true;
function animate() {
  requestAnimationFrame(animate);
  controls.update();
  if (state.selectedId || state.faultActive || state.mode === 'normal') applyMaterialState();
  updateLabels();
  renderer.render(scene, camera);
  if (firstFrame) { firstFrame = false; loadingEl.classList.add('hidden'); }
}
animate();
