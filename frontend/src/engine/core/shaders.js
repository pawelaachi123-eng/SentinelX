import { NOISE, FIELD } from "./glsl";

const COMMON_UNIFORMS = /* glsl */ `
uniform float uLayer; uniform float uScale;
uniform float uEnergy; uniform float uGlow; uniform float uSat; uniform float uErr; uniform float uLightDyn;
uniform vec3 uColA; uniform vec3 uColB; uniform vec3 uColDeep; uniform vec3 uColErr;
uniform vec3 uKey;
`;

export const coreVertex = /* glsl */ `
${NOISE}
${FIELD}
${COMMON_UNIFORMS}
varying vec3 vN; varying vec3 vV; varying vec3 vLocal; varying float vDisp; varying float vCurv;
void main(){
  vec3 p = normalize(position);
  vec3 t1 = normalize(cross(p, abs(p.y)<0.99 ? vec3(0.0,1.0,0.0) : vec3(1.0,0.0,0.0)));
  vec3 t2 = cross(p, t1);
  float e = 0.012;
  vec3 P  = deform(p);
  vec3 Pa = deform(normalize(p+t1*e));
  vec3 Pb = deform(normalize(p+t2*e));
  vec3 n = normalize(cross(Pa-P, Pb-P));
  if(dot(n,p)<0.0) n=-n;
  vDisp = length(P/uAniso)-1.0;
  vCurv = clamp((length(Pa/uAniso)+length(Pb/uAniso))*0.5-1.0-vDisp, -0.02, 0.02)*50.0;
  vLocal = p;
  vec4 mv = modelViewMatrix*vec4(P*uScale,1.0);
  vN = normalize(normalMatrix*n);
  vV = -mv.xyz;
  gl_Position = projectionMatrix*mv;
}`;

export const coreFragment = /* glsl */ `
${NOISE}
${COMMON_UNIFORMS}
uniform float uTB; uniform float uTC; uniform float uPulse; uniform float uPulseAmt; uniform vec3 uPulseAxis; uniform float uImpulse;
varying vec3 vN; varying vec3 vV; varying vec3 vLocal; varying float vDisp; varying float vCurv;
vec3 desat(vec3 c, float s){ float l=dot(c,vec3(0.299,0.587,0.114)); return mix(vec3(l),c,s); }
void main(){
  vec3 N = normalize(vN); vec3 V = normalize(vV);
  float ndv = clamp(dot(N,V),0.0,1.0);
  float fres = pow(1.0-ndv, 2.4);
  vec3 L = normalize(uKey); vec3 Lr = normalize(vec3(-0.7,0.25,-0.6));
  vec3 colA = mix(uColA, uColErr, uErr*0.75);
  // energy veins flowing through the structure (L4)
  float vein = 1.0-abs(snoise(vLocal*2.6+vec3(0.0,uTB*0.55,uTC*0.25)));
  vein = pow(vein, 9.0) * (0.55+0.45*snoise(vLocal*1.3-vec3(uTB*0.3)));
  float band = exp(-pow((dot(vLocal,uPulseAxis)-(fract(uPulse)*2.6-1.3))*4.0,2.0))*uPulseAmt;
  float cav = smoothstep(0.12,-0.12,vDisp);          // concavities
  float ridge = smoothstep(0.05,0.32,vDisp);         // protrusions
  if(uLayer < 0.5){
    float wrap = pow(clamp(dot(N,L)*0.5+0.5,0.0,1.0),1.8);
    vec3 H = normalize(L+V);
    float spec = pow(max(dot(N,H),0.0), 56.0)*(0.55+0.45*ridge);
    float rim = pow(max(dot(N,Lr),0.0),2.0)*fres;
    vec3 base = mix(uColDeep, mix(uColDeep, colA, 0.35), wrap*(0.6+0.4*ridge));
    vec3 sss = uColB*cav*(0.35+0.65*fres)*0.9*uGlow;            // subsurface-inspired inner glow
    vec3 col = base*(0.55+0.45*uLightDyn*wrap);
    col += sss;
    col += colA*fres*(0.55+0.6*uGlow);
    col += uColB*rim*1.2;
    col += vec3(0.86,0.95,1.0)*spec*(0.5+0.5*uLightDyn);
    col += colA*vein*uEnergy*(0.9+0.6*cav);
    col += mix(colA,vec3(1.0),0.4)*band*0.9;
    col += colA*uImpulse*0.5*fres;
    col *= 0.92+0.16*vCurv;
    col = desat(col, uSat);
    float alpha = mix(0.62, 0.97, fres)+cav*0.05;
    gl_FragColor = vec4(col, clamp(alpha,0.0,1.0));
  } else if(uLayer < 1.5){
    // outer membrane: fresnel-only additive veil
    float a = pow(1.0-ndv,3.2)*0.55*(0.6+0.4*uGlow);
    vec3 col = mix(uColB, colA, 0.5+0.5*snoise(vLocal*1.6+vec3(uTB*0.2)));
    gl_FragColor = vec4(desat(col,uSat)*a, a);
  } else {
    // inner energy nucleus
    float core = pow(ndv,1.6);
    float flow = 0.5+0.5*snoise(vLocal*3.4+vec3(uTC*0.6,uTB,0.0));
    vec3 col = mix(uColB, colA, flow)*(0.35+0.9*core)*(0.35+0.85*uEnergy);
    gl_FragColor = vec4(desat(col,uSat), 1.0);
  }
}`;

export const particleVertex = /* glsl */ `
${NOISE}
${FIELD}
uniform float uScale; uniform float uPx; uniform float uEnergy;
attribute vec4 aSeed;
varying float vA;
void main(){
  vec3 d = normalize(position);
  float t = uTB*(0.25+aSeed.x*0.5);
  vec3 drift = vec3(snoise(d*1.3+vec3(t,0.0,aSeed.y*9.0)), snoise(d*1.3+vec3(0.0,t,aSeed.z*7.0)), snoise(d*1.3+vec3(aSeed.w*5.0,0.0,t)));
  d = normalize(d + drift*0.55);
  float surf = 1.0 + field(d);
  float shell = 0.06 + aSeed.y*0.55 + 0.08*sin(uTA*3.0+aSeed.z*30.0);
  vec3 P = d*(surf+shell)*uAniso*uScale;
  vec4 mv = modelViewMatrix*vec4(P,1.0);
  gl_Position = projectionMatrix*mv;
  gl_PointSize = uPx*(1.0+aSeed.w*1.6)*(3.0/-mv.z);
  vA = (1.0-smoothstep(0.1,0.65,shell))*(0.35+0.65*uEnergy)*(0.5+0.5*aSeed.x);
}`;

export const particleFragment = /* glsl */ `
uniform vec3 uColA; uniform vec3 uColB; uniform float uSat;
varying float vA;
void main(){
  vec2 c = gl_PointCoord-0.5; float r = length(c);
  float a = smoothstep(0.5,0.0,r)*vA;
  vec3 col = mix(uColB,uColA,smoothstep(0.0,0.5,vA));
  float l = dot(col,vec3(0.299,0.587,0.114));
  gl_FragColor = vec4(mix(vec3(l),col,uSat)*a, a);
}`;

export const bgVertex = /* glsl */ `varying vec2 vUv; void main(){ vUv=uv; gl_Position=vec4(position.xy,0.0,1.0); }`;

export const bgFragment = /* glsl */ `
${NOISE}
uniform float uT; uniform vec2 uRes; uniform vec3 uColA; uniform vec3 uColB; uniform vec3 uBg; uniform float uGlow; uniform vec2 uCenter; uniform float uSat;
varying vec2 vUv;
void main(){
  vec2 asp = vec2(uRes.x/uRes.y,1.0);
  vec2 p = (vUv-uCenter)*asp;
  float r = length(p);
  float fog = fbm3(vec3(p*1.4, uT*0.05))*0.5+0.5;
  vec3 col = uBg + vec3(0.010,0.018,0.035)*(1.0-vUv.y);
  float halo = exp(-r*r*3.2)*(0.20+0.18*uGlow);
  col += mix(uColB,uColA,0.45+0.25*sin(uT*0.07))*halo*(0.55+0.45*fog);
  col += uColB*0.035*exp(-pow(length((vUv-vec2(0.12,0.92))*asp),2.0)*2.5);
  col += uColA*0.025*exp(-pow(length((vUv-vec2(0.92,0.1))*asp),2.0)*2.0);
  col *= 1.0-0.55*smoothstep(0.55,1.45,length((vUv-0.5)*asp));
  float g = fract(sin(dot(vUv*uRes+uT,vec2(12.9898,78.233)))*43758.5453);
  col += (g-0.5)*0.012;
  float l = dot(col,vec3(0.299,0.587,0.114));
  gl_FragColor = vec4(mix(vec3(l),col,mix(0.6,1.0,uSat)),1.0);
}`;
