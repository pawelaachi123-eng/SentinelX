// Shared GLSL: simplex noise, fbm, domain warping and the procedural deformation field.
export const NOISE = /* glsl */ `
vec3 mod289(vec3 x){return x-floor(x*(1.0/289.0))*289.0;}
vec4 mod289(vec4 x){return x-floor(x*(1.0/289.0))*289.0;}
vec4 permute(vec4 x){return mod289(((x*34.0)+1.0)*x);}
vec4 taylorInvSqrt(vec4 r){return 1.79284291400159-0.85373472095314*r;}
float snoise(vec3 v){
  const vec2 C=vec2(1.0/6.0,1.0/3.0);const vec4 D=vec4(0.0,0.5,1.0,2.0);
  vec3 i=floor(v+dot(v,C.yyy));vec3 x0=v-i+dot(i,C.xxx);
  vec3 g=step(x0.yzx,x0.xyz);vec3 l=1.0-g;vec3 i1=min(g.xyz,l.zxy);vec3 i2=max(g.xyz,l.zxy);
  vec3 x1=x0-i1+C.xxx;vec3 x2=x0-i2+C.yyy;vec3 x3=x0-D.yyy;
  i=mod289(i);
  vec4 p=permute(permute(permute(i.z+vec4(0.0,i1.z,i2.z,1.0))+i.y+vec4(0.0,i1.y,i2.y,1.0))+i.x+vec4(0.0,i1.x,i2.x,1.0));
  float n_=0.142857142857;vec3 ns=n_*D.wyz-D.xzx;
  vec4 j=p-49.0*floor(p*ns.z*ns.z);vec4 x_=floor(j*ns.z);vec4 y_=floor(j-7.0*x_);
  vec4 x=x_*ns.x+ns.yyyy;vec4 y=y_*ns.x+ns.yyyy;vec4 h=1.0-abs(x)-abs(y);
  vec4 b0=vec4(x.xy,y.xy);vec4 b1=vec4(x.zw,y.zw);
  vec4 s0=floor(b0)*2.0+1.0;vec4 s1=floor(b1)*2.0+1.0;vec4 sh=-step(h,vec4(0.0));
  vec4 a0=b0.xzyw+s0.xzyw*sh.xxyy;vec4 a1=b1.xzyw+s1.xzyw*sh.zzww;
  vec3 p0=vec3(a0.xy,h.x);vec3 p1=vec3(a0.zw,h.y);vec3 p2=vec3(a1.xy,h.z);vec3 p3=vec3(a1.zw,h.w);
  vec4 norm=taylorInvSqrt(vec4(dot(p0,p0),dot(p1,p1),dot(p2,p2),dot(p3,p3)));
  p0*=norm.x;p1*=norm.y;p2*=norm.z;p3*=norm.w;
  vec4 m=max(0.6-vec4(dot(x0,x0),dot(x1,x1),dot(x2,x2),dot(x3,x3)),0.0);m=m*m;
  return 42.0*dot(m*m,vec4(dot(p0,x0),dot(p1,x1),dot(p2,x2),dot(p3,x3)));
}
float fbm3(vec3 p){float a=0.5,s=0.0;for(int i=0;i<3;i++){s+=a*snoise(p);p=p*2.03+vec3(1.7,9.2,3.1);a*=0.5;}return s;}
float fbm4(vec3 p){float a=0.5,s=0.0;for(int i=0;i<4;i++){s+=a*snoise(p);p=p*1.97+vec3(5.2,1.3,7.7);a*=0.5;}return s;}
`;

// Deformation field. All time inputs are integrated clocks (never reset) -> continuity across states.
export const FIELD = /* glsl */ `
uniform float uTA; uniform float uTB; uniform float uTC;
uniform float uMacro; uniform float uMeso; uniform float uMicro; uniform float uTension;
uniform float uSeed;
uniform vec4 uAttr[5];   // xyz dir, w signed strength (bulge / cave)
uniform vec4 uLobe[3];   // xyz dir, w amplitude (asymmetric limbs)
uniform vec3 uPtr; uniform float uPtrAmt;
uniform vec4 uRip[4];    // xyz dir, w age (s)
uniform vec3 uPulseAxis; uniform float uPulse; uniform float uPulseAmt;
uniform float uImpulse;
uniform vec3 uAudio;
uniform vec3 uAniso;

float field(vec3 p){
  vec3 s = p + vec3(uSeed);
  // L1 macro morphing: domain-warped low frequency
  vec3 w = vec3(snoise(s*0.8+vec3(uTA,0.0,0.0)), snoise(s*0.8+vec3(0.0,uTA*0.9,4.1)), snoise(s*0.8+vec3(7.3,0.0,uTA*1.1)));
  float macro = fbm3(s*1.05 + w*0.55 + vec3(0.0,uTA*0.6,0.0));
  // asymmetric base limbs (structure is asymmetric before any animation)
  float limbs = 0.0;
  for(int i=0;i<3;i++){ float d=max(dot(p,uLobe[i].xyz),0.0); limbs += uLobe[i].w*(pow(d,4.0)*0.55+pow(d,16.0)*0.8); }
  limbs -= 0.13*pow(max(dot(p,normalize(vec3(-0.3,-0.8,0.5))),0.0),3.0);
  // L2 meso: independent localized deformation fields
  float meso = 0.0;
  for(int i=0;i<5;i++){ float c=dot(p,uAttr[i].xyz); meso += uAttr[i].w*exp((c-1.0)*5.5); }
  meso += 0.45*fbm3(s*1.7 + vec3(uTB*0.7,-uTB*0.4,uTB*0.3) + w*0.3);
  // L3 micro: travelling surface ripples
  float micro = snoise(s*4.0 + vec3(uTC*1.3,uTC*0.7,-uTC)) * 0.7 + snoise(s*7.5 - vec3(uTC*1.9)) * 0.25;
  micro *= 1.0 + uTension*1.4;
  // L5 interaction
  float ptr = uPtrAmt*pow(max(dot(p,uPtr),0.0),9.0);
  float rip = 0.0;
  for(int i=0;i<4;i++){ float age=uRip[i].w; if(age<4.0){ float ang=acos(clamp(dot(p,uRip[i].xyz),-1.0,1.0)); rip += sin((ang-age*1.8)*11.0)*exp(-pow((ang-age*1.8)*3.2,2.0))*exp(-age*1.1); } }
  float pulse = uPulseAmt*exp(-pow((dot(p,uPulseAxis)-(fract(uPulse)*2.6-1.3))*5.0,2.0));
  float audio = 0.0;
  if(uAudio.x+uAudio.y+uAudio.z > 0.002) audio = uAudio.x*0.16*snoise(s*2.4+vec3(uTB*2.0)) + uAudio.y*0.07*snoise(s*5.0-vec3(uTC)) + uAudio.z*0.035*snoise(s*10.0+vec3(uTC*3.0));
  return 0.36*uMacro*macro + limbs + 0.22*uMeso*meso + 0.016*uMicro*micro
       + 0.10*ptr + 0.07*rip + 0.085*pulse + 0.09*uImpulse*macro + audio;
}
vec3 deform(vec3 p){ return p*(1.0+field(p))*uAniso; }
`;
