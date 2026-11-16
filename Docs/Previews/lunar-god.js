import {fit,sparkle} from '../../core/index.js';
// Original colour/motion study; no copied Sprite Fusion recipe.
export const meta={name:'Nine lunar gates',desc:'Nine cream moons gather in a violet arch, launch in sequence and dissolve into rose stars.',tags:['moon','ultimate'],size:[288,192],origin:[100,124],duration:2.8,loop:false,fps:30,bg:'dark',options:{palette:['#514b91','#948bd1','#d6bde6','#f3dba6','#fff1c9','#fffdf0'],seed:1}};
function moon(p,cx,cy,r,C,alpha=1){
 for(let y=-r;y<=r;y++)for(let x=-r;x<=r;x++){
  const d=(x*x+y*y)/(r*r);if(d>1)continue;
  const crater=(x+r*.3)**2+(y-r*.12)**2<r*r*.045 || (x-r*.25)**2+(y+r*.35)**2<r*r*.025;
  const c=C[d>.88?1:d>.72?5:crater?2:y<0?4:3];p.blend(cx+x,cy+y,c,alpha);
 }
}
export function draw(p,t,o){
 if(t>=meta.duration)return;const C=fit(o.palette,6);
 for(let i=0;i<9;i++){
  const a=(20+i*17.5)*Math.PI/180,x=Math.cos(a)*65,y=-Math.sin(a)*43-20;
  const launch=.8+i*.12,age=t-launch;
  if(age<0){moon(p,x,y,8,C,Math.min(1,t/.65));continue;}
  if(age<.55){const k=age/.55,xx=x+(140-x)*k,yy=y+(14-y)*k;
   const tail=.08;moon(p,xx-(140-x)*tail,yy-(14-y)*tail,7,C,.22);
   p.glowLine(xx-8,yy-3,xx,yy,C[1],.12,3);moon(p,xx,yy,8,C);p.glow(xx,yy,10,C[4],.16);
  }else{const k=(age-.55)/.35;if(k<1){sparkle(p,140,14,9*(1-k),C[5],C[2]);p.ring(140,14,4+14*k,1,C[1],1-k);}}
 }
}
