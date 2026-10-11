import * as THREE from "three";
import { STATE_PARAMS, BLEND_RATE, damp } from "./states";

// Smooth, coherent (non-random-per-frame) drivers for attractors and limbs.
const AF = [[0.13, 0.17, 0.11, 0.07], [0.09, 0.21, 0.15, 0.053], [0.19, 0.08, 0.12, 0.091], [0.11, 0.14, 0.23, 0.037], [0.16, 0.12, 0.07, 0.069]];
const LOBES = [[0.62, 0.55, 0.3, 0.44], [-0.7, 0.1, 0.55, 0.3], [0.15, -0.75, -0.45, 0.38]];

export class Choreographer {
  constructor(u) {
    this.u = u;
    this.p = { ...STATE_PARAMS.idle };
    this.target = STATE_PARAMS.idle;
    this.state = "idle";
    this.clock = { a: 0, b: 0, c: 0, pulse: 0, wall: 0 };
    this.impulse = 0;
    this.ripples = [];
    this.ptrTarget = new THREE.Vector3(0, 0, 1);
    this.ptrAmtTarget = 0;
    this.audio = [0, 0, 0];
    this.custom = { deform: 1, macro: 1, meso: 1, micro: 1, light: 1, energy: 1, speed: 1, reduced: false };
    this._v = new THREE.Vector3();
  }

  setState(s) {
    if (s === this.state) return;
    if (s === "success") this.impulse = 1;
    this.state = s;
    this.target = STATE_PARAMS[s] || STATE_PARAMS.idle;
  }

  ripple(dir) {
    this.ripples.unshift({ dir: dir.clone().normalize(), age: 0 });
    this.ripples.length = Math.min(this.ripples.length, 4);
  }

  step(dt) {
    const { u, p, target, clock, custom } = this;
    const rate = BLEND_RATE[this.state] || BLEND_RATE.default;
    for (const k in target) p[k] = damp(p[k], target[k], rate, dt);
    const rm = custom.reduced ? 0.45 : 1;
    const sp = p.speed * custom.speed * rm;
    // independent clocks per layer (L1 macro / L2 meso / L3 micro)
    clock.a += dt * 0.11 * sp;
    clock.b += dt * 0.23 * sp;
    clock.c += dt * 0.52 * sp;
    clock.pulse += dt * 0.55 * (0.4 + p.pulse);
    clock.wall += dt;
    const t = clock.a * 4.0;

    // L2 attractors: incommensurate smooth orbits; signed strength => bulge or cave
    for (let i = 0; i < 5; i++) {
      const f = AF[i];
      this._v.set(Math.sin(t * f[0] * 6 + i * 1.7), Math.cos(t * f[1] * 6 + i * 2.3), Math.sin(t * f[2] * 6 + i * 0.9 + 1.1)).normalize();
      const s = Math.sin(t * f[3] * 9 + i * 2.1) * 0.6 + Math.sin(t * f[3] * 3.7 + i) * 0.4;
      u.uAttr.value[i].set(this._v.x, this._v.y, this._v.z, s * 0.9);
    }
    // L6 emergent: limbs wander slowly; amplitude evolves from two beating sines
    for (let i = 0; i < 3; i++) {
      const L = LOBES[i];
      this._v.set(L[0] + 0.35 * Math.sin(t * 0.21 + i * 2.0), L[1] + 0.35 * Math.cos(t * 0.17 + i), L[2] + 0.3 * Math.sin(t * 0.13 + i * 3.1)).normalize();
      const amp = L[3] * (0.75 + 0.25 * Math.sin(t * 0.31 + i * 1.3) * Math.cos(t * 0.077 + i));
      u.uLobe.value[i].set(this._v.x, this._v.y, this._v.z, amp * custom.deform);
    }
    u.uAniso.value.set(1.14 + 0.08 * Math.sin(t * 0.19), 0.88 + 0.07 * Math.sin(t * 0.23 + 1.0), 1.0 + 0.05 * Math.cos(t * 0.15));

    // ripples & interaction
    for (let i = 0; i < 4; i++) {
      const r = this.ripples[i];
      if (r) { r.age += dt; u.uRip.value[i].set(r.dir.x, r.dir.y, r.dir.z, r.age); }
      else u.uRip.value[i].w = 10;
    }
    this.ripples = this.ripples.filter((r) => r.age < 4);
    u.uPtr.value.lerp(this.ptrTarget, 1 - Math.exp(-6 * dt)).normalize();
    u.uPtrAmt.value = damp(u.uPtrAmt.value, this.ptrAmtTarget, 4, dt);
    this.impulse = damp(this.impulse, 0, 1.6, dt);
    const a = u.uAudio.value;
    a.set(damp(a.x, this.audio[0], 10, dt), damp(a.y, this.audio[1], 12, dt), damp(a.z, this.audio[2], 14, dt));

    u.uTA.value = clock.a; u.uTB.value = clock.b; u.uTC.value = clock.c;
    u.uPulse.value = clock.pulse;
    u.uPulseAxis.value.set(Math.sin(t * 0.3), 0.6, Math.cos(t * 0.3)).normalize();
    u.uMacro.value = p.macro * custom.deform * custom.macro * rm;
    u.uMeso.value = p.meso * custom.deform * custom.meso * rm;
    u.uMicro.value = p.micro * custom.deform * custom.micro * (custom.reduced ? 0.6 : 1);
    u.uTension.value = p.tension;
    u.uPulseAmt.value = p.pulse;
    u.uImpulse.value = this.impulse;
    u.uEnergy.value = p.energy * custom.energy;
    u.uGlow.value = p.glow;
    u.uSat.value = p.sat;
    u.uErr.value = p.err;
    u.uLightDyn.value = custom.light;
    u.uKey.value.set(Math.sin(t * 0.09) * 0.8 + 0.4, 0.75 + 0.2 * Math.sin(t * 0.13), 0.8);
    return p;
  }
}
