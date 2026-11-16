import { fit, sparkle } from "../../core/index.js";

// Original moon silhouette study for the WorldBox Huyen Nguyet extension.
export const meta = { name: "Huyen Nguyet", desc: "Cream crescent, violet echoes and a sharp rose impact.", tags: ["moon", "projectile"], size: [256,128], origin: [48,64], duration: 1.6, loop: false, fps: 30, bg: "dark", options: { palette: ["#514b91","#948bd1","#d6bde6","#f3dba6","#fff1c9","#fffdf0"], seed: 1 } };
function moon(p,cx,cy,r,C,a) {
  p.shade(cx-r-1,cy-r-1,cx+r+1,cy+r+1,(x,y,out)=>{
    const dx=x-cx,dy=y-cy,d=Math.hypot(dx,dy),cut=Math.hypot(dx+r*.40,dy);
    if(d>r || cut<r*.91)return;
    const edge=r-d,inner=cut-r*.91;
    const color=C[edge<1?0:inner<1.4?2:edge<3?5:dy<0?4:3];
    if(a<1){p.blend(x,y,C[1],a);return;}out.c=color;out.a=1;
  });
}
export function draw(p,t,o) {
  if(t>=meta.duration)return;
  const C=fit(o.palette,6), launch=.3, hit=1.02;
  if(t<launch) {
    const k=t/launch; moon(p,0,0,5+13*k,C,1);
    for(let i=0;i<4;i++){const a=i*Math.PI/2+k*2,r=30*(1-k)+8;sparkle(p,Math.cos(a)*r,Math.sin(a)*r,2,C[5],C[1]);}
  } else if(t<hit) {
    const x=(t-launch)/(hit-launch)*145;
    for(let j=3;j>=1;j--)moon(p,x-j*9,0,24,C,.32*(1-j/4));
    moon(p,x,0,24,C,1);p.glow(x+18,0,17,C[4],.15);
  } else {
    const k=(t-hit)/(meta.duration-hit),r=5+35*k;
    p.ring(145,0,r,1,C[1],1-k);
    // Violet secondary shock follows the hot four-point hero frame.
    if(k>.08)p.ring(145,0,r*.65,1,C[2],(1-k)*.55);
    sparkle(p,145,0,24*Math.pow(1-k,2),C[5],C[2]);
    for(let i=0;i<6;i++){const a=i*Math.PI/3+.3;sparkle(p,145+Math.cos(a)*r,Math.sin(a)*r,3*(1-k),C[4],C[1],1-k);}
  }
}
