import * as THREE from "three";
import { mergeVertices } from "three/examples/jsm/utils/BufferGeometryUtils.js";
import { EffectComposer } from "three/examples/jsm/postprocessing/EffectComposer.js";
import { RenderPass } from "three/examples/jsm/postprocessing/RenderPass.js";
import { UnrealBloomPass } from "three/examples/jsm/postprocessing/UnrealBloomPass.js";
import { OutputPass } from "three/examples/jsm/postprocessing/OutputPass.js";
import { coreVertex, coreFragment, particleVertex, particleFragment, bgVertex, bgFragment } from "./shaders";
import { Choreographer } from "./choreographer";

const TIERS = {
  high: { detail: 80, particles: 1.0, bloom: true, pr: 2 },
  medium: { detail: 64, particles: 0.6, bloom: true, pr: 1.5 },
  low: { detail: 36, particles: 0.3, bloom: false, pr: 0.7 },
};
const ORDER = ["low", "medium", "high"];
const hex = (h) => new THREE.Color(h);

function sphere(detail) {
  const g = new THREE.IcosahedronGeometry(1, detail);
  g.deleteAttribute("normal"); g.deleteAttribute("uv");
  return mergeVertices(g);
}

/** Independent GPU rendering layer. Talks to the app only through setState/setOptions/events. */
export class CoreRenderer {
  constructor(canvas, opts = {}) {
    this.canvas = canvas;
    this.opts = { quality: "auto", bloom: true, particles: 1, prCap: 2, palette: null, ...opts };
    this.stats = { fps: 0, frameMs: 0, tier: "high", contextLosses: 0, gpu: "", vertices: 0 };
    this.listeners = new Set();
    this._onLost = (e) => { e.preventDefault(); this.stats.contextLosses++; this._stop(); this._emit(); };
    this._onRestored = () => { this._teardown(); this._init(); this.start(); };
    canvas.addEventListener("webglcontextlost", this._onLost);
    canvas.addEventListener("webglcontextrestored", this._onRestored);
    this.tier = this.opts.quality === "auto" ? "medium" : this.opts.quality;
    this._init();
  }

  _uniforms() {
    const v4 = (n) => Array.from({ length: n }, () => new THREE.Vector4(0, 0, 1, 0));
    const pal = this.opts.palette;
    return {
      uTA: { value: 0 }, uTB: { value: 0 }, uTC: { value: 0 }, uMacro: { value: 1 }, uMeso: { value: 1 }, uMicro: { value: 1 },
      uTension: { value: 0 }, uSeed: { value: 0 }, uAttr: { value: v4(5) }, uLobe: { value: v4(3) },
      uPtr: { value: new THREE.Vector3(0, 0, 1) }, uPtrAmt: { value: 0 }, uRip: { value: v4(4).map((v) => v.setW(10)) },
      uPulseAxis: { value: new THREE.Vector3(0, 1, 0) }, uPulse: { value: 0 }, uPulseAmt: { value: 0 }, uImpulse: { value: 0 },
      uAudio: { value: new THREE.Vector3() }, uAniso: { value: new THREE.Vector3(1, 1, 1) },
      uLayer: { value: 0 }, uScale: { value: 1 }, uEnergy: { value: 0.5 }, uGlow: { value: 0.5 }, uSat: { value: 1 }, uErr: { value: 0 }, uLightDyn: { value: 1 },
      uColA: { value: hex(pal?.a || "#4CC6D8") }, uColB: { value: hex(pal?.b || "#5E7FD8") }, uColDeep: { value: hex(pal?.deep || "#072642") }, uColErr: { value: hex("#E88AA0") },
      uKey: { value: new THREE.Vector3(0.5, 0.8, 0.8) }, uPx: { value: 2 },
    };
  }

  _init() {
    const r = new THREE.WebGLRenderer({ canvas: this.canvas, antialias: true, alpha: false, powerPreference: "high-performance" });
    this.renderer = r;
    const gl = r.getContext();
    const ext = gl.getExtension("WEBGL_debug_renderer_info");
    this.stats.gpu = ext ? gl.getParameter(ext.UNMASKED_RENDERER_WEBGL) : "WebGL2";
    r.setClearColor("#04162A", 1);
    this.scene = new THREE.Scene();
    this.camera = new THREE.PerspectiveCamera(32, 1, 0.1, 50);
    this.camera.position.set(0, 0, 6.2);
    this.u = this._uniforms();
    const prev = this._prevChoreo;
    this.choreo = new Choreographer(this.u);
    if (prev) { Object.assign(this.choreo.clock, prev.clock); this.choreo.setState(prev.state); this.choreo.custom = prev.custom; }

    const bgMat = new THREE.ShaderMaterial({ vertexShader: bgVertex, fragmentShader: bgFragment, depthTest: false, depthWrite: false,
      uniforms: { uT: { value: 0 }, uRes: { value: new THREE.Vector2(1, 1) }, uColA: this.u.uColA, uColB: this.u.uColB, uBg: { value: hex("#04162A") }, uCenter: { value: new THREE.Vector2(0.5, 0.52) } } });
    this.bg = new THREE.Mesh(new THREE.PlaneGeometry(2, 2), bgMat);
    this.bg.frustumCulled = false; this.bg.renderOrder = -10;
    this.scene.add(this.bg);

    this.group = new THREE.Group();
    this.scene.add(this.group);
    this._buildMeshes();

    this.composer = new EffectComposer(r);
    this.composer.addPass(new RenderPass(this.scene, this.camera));
    this.bloom = new UnrealBloomPass(new THREE.Vector2(256, 256), 0.55, 0.6, 0.62);
    this.composer.addPass(this.bloom);
    this.composer.addPass(new OutputPass());
    this.resize();
    this._emit();
  }

  _layer(layer, scale, detail, blending, order) {
    const m = new THREE.ShaderMaterial({
      vertexShader: coreVertex, fragmentShader: coreFragment, transparent: true, blending, depthWrite: layer === 0,
      uniforms: { ...this.u, uLayer: { value: layer }, uScale: { value: scale }, uSeed: { value: layer * 3.7 } },
    });
    const mesh = new THREE.Mesh(sphere(detail), m);
    mesh.renderOrder = order; mesh.frustumCulled = false;
    return mesh;
  }

  _buildMeshes() {
    this.group.children.slice().forEach((c) => { c.geometry.dispose(); c.material.dispose(); this.group.remove(c); });
    const T = TIERS[this.tier];
    this.body = this._layer(0, 1, T.detail, THREE.NormalBlending, 2);
    this.veil = this._layer(1, 1.14, Math.round(T.detail * 0.5), THREE.AdditiveBlending, 3);
    const n = Math.round(900 * T.particles * this.opts.particles);
    const pos = new Float32Array(n * 3), seed = new Float32Array(n * 4);
    for (let i = 0; i < n; i++) {
      const v = new THREE.Vector3().randomDirection();
      pos.set([v.x, v.y, v.z], i * 3);
      seed.set([Math.random(), Math.random() ** 1.6, Math.random(), Math.random() ** 3], i * 4);
    }
    const pg = new THREE.BufferGeometry();
    pg.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    pg.setAttribute("aSeed", new THREE.BufferAttribute(seed, 4));
    this.particles = new THREE.Points(pg, new THREE.ShaderMaterial({ vertexShader: particleVertex, fragmentShader: particleFragment,
      transparent: true, depthWrite: false, blending: THREE.AdditiveBlending, uniforms: { ...this.u, uScale: { value: 1 }, uSeed: { value: 0 } } }));
    this.particles.renderOrder = 4; this.particles.frustumCulled = false;
    this.group.add(this.body, this.veil, this.particles);
    this.stats.vertices = this.body.geometry.attributes.position.count;
  }

  setOptions(o) {
    const prev = this.opts;
    this.opts = { ...prev, ...o };
    if (o.palette) { this.u.uColA.value.set(o.palette.a); this.u.uColB.value.set(o.palette.b); if (o.palette.deep) this.u.uColDeep.value.set(o.palette.deep); }
    if (o.custom) Object.assign(this.choreo.custom, o.custom);
    if (o.quality && o.quality !== prev.quality && o.quality !== "auto") this._setTier(o.quality);
    if (o.particles !== undefined && o.particles !== prev.particles) this._buildMeshes();
    if (o.prCap !== undefined) this.resize();
  }

  _setTier(t) { if (t === this.tier) return; this.tier = t; this.stats.tier = t; this._buildMeshes(); this.resize(); this._emit(); }

  setState(s) { this.choreo.setState(s); }
  setAudio(lo, mid, hi) { this.choreo.audio = [lo, mid, hi]; }

  _dirFromClient(x, y) {
    const rect = this.canvas.getBoundingClientRect();
    const ndc = new THREE.Vector2(((x - rect.left) / rect.width) * 2 - 1, -((y - rect.top) / rect.height) * 2 + 1);
    const ray = new THREE.Raycaster(); ray.setFromCamera(ndc, this.camera);
    const hit = new THREE.Vector3();
    const sph = new THREE.Sphere(new THREE.Vector3(), 1.25);
    if (!ray.ray.intersectSphere(sph, hit)) return null;
    return hit.applyMatrix4(this.group.matrixWorld.clone().invert()).normalize();
  }
  pointer(x, y) { const d = this._dirFromClient(x, y); this.choreo.ptrAmtTarget = d ? 1 : 0; if (d) this.choreo.ptrTarget.copy(d); }
  pointerLeave() { this.choreo.ptrAmtTarget = 0; }
  click(x, y) { const d = this._dirFromClient(x, y); if (d) { this.choreo.ripple(d); return true; } return false; }

  resize() {
    if (!this.renderer) return;
    const w = this.canvas.clientWidth || window.innerWidth, h = this.canvas.clientHeight || window.innerHeight;
    const pr = Math.min(window.devicePixelRatio || 1, TIERS[this.tier].pr, this.opts.prCap);
    this.renderer.setPixelRatio(pr);
    this.renderer.setSize(w, h, false);
    this.composer.setPixelRatio(pr); this.composer.setSize(w, h);
    this.bloom.resolution.set(w / 2, h / 2);
    this.camera.aspect = w / h;
    // keep core dominant but leave room for UI: fit by min dimension
    this.camera.position.z = w / h < 1 ? 6.2 / Math.max(w / h, 0.55) : 6.2;
    this.camera.updateProjectionMatrix();
    this.bg.material.uniforms.uRes.value.set(w, h);
    this.u.uPx.value = 2.2 * pr;
  }

  _adapt(dt) {
    const s = this.stats;
    this._acc = (this._acc || 0) + dt; this._frames = (this._frames || 0) + 1;
    if (this._acc < 1) return;
    s.fps = Math.round(this._frames / this._acc); s.frameMs = +((this._acc / this._frames) * 1000).toFixed(1);
    this._acc = 0; this._frames = 0; this._emit();
    if (this.opts.quality !== "auto") return;
    this._slow = s.fps < 45 ? (this._slow || 0) + (s.fps < 24 ? 3 : 1) : 0;
    this._fast = s.fps >= 58 ? (this._fast || 0) + 1 : 0;
    const i = ORDER.indexOf(this.tier);
    if (this._slow >= 3 && i > 0) { this._slow = 0; this._setTier(ORDER[i - 1]); }
    if (this._fast >= 8 && i < 2) { this._fast = 0; this._setTier(ORDER[i + 1]); }
  }

  _frame = (now) => {
    this._raf = requestAnimationFrame(this._frame);
    const dt = Math.min((now - (this._last || now)) / 1000, 0.05);
    this._last = now;
    const p = this.choreo.step(dt);
    const t = this.choreo.clock.a * 4;
    this.group.rotation.set(0.25 * Math.sin(t * 0.11) + 0.1, t * 0.35, 0.12 * Math.cos(t * 0.07));
    this.group.position.y = 0.04 * Math.sin(t * 0.21);
    this.bg.material.uniforms.uT.value = this.choreo.clock.wall;
    const useBloom = this.opts.bloom && TIERS[this.tier].bloom;
    if (useBloom) { this.bloom.strength = 0.18 + 0.2 * p.glow; this.composer.render(); }
    else this.renderer.render(this.scene, this.camera);
    this._adapt(dt);
  };

  start() { if (!this._raf && this.renderer) { this._last = 0; this._raf = requestAnimationFrame(this._frame); } }
  _stop() { if (this._raf) cancelAnimationFrame(this._raf); this._raf = null; }
  pause() { this._stop(); }

  onStats(fn) { this.listeners.add(fn); return () => this.listeners.delete(fn); }
  _emit() { this.stats.tier = this.tier; this.listeners.forEach((f) => f({ ...this.stats })); }

  _teardown() {
    this._stop();
    this._prevChoreo = this.choreo;
    this.group?.children.forEach((c) => { c.geometry.dispose(); c.material.dispose(); });
    this.bg?.geometry.dispose(); this.bg?.material.dispose();
    this.composer?.dispose?.();
    this.renderer?.dispose();
  }

  dispose() {
    this._teardown();
    this.canvas.removeEventListener("webglcontextlost", this._onLost);
    this.canvas.removeEventListener("webglcontextrestored", this._onRestored);
    this.listeners.clear();
  }
}
