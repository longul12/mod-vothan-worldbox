using UnityEngine;

namespace Sakuga
{
    // Original, finite procedural sprite bank. Never builds textures in a tick loop.
    internal static class MoonArt
    {
        private static readonly Color32[] Ramp = {
            new Color32(81,75,145,255), new Color32(148,139,209,255),
            new Color32(214,189,230,255), new Color32(243,219,166,255),
            new Color32(255,241,201,255), new Color32(255,253,240,255)
        };
        private static readonly Sprite[][] Crescents = new Sprite[3][];
        private static Sprite _moon, _star, _palm;
        private static Sprite[] _moonFrames,_palmFrames;
        public static readonly Color Rose=new Color(.94f,.55f,.84f);
        internal static Canvas BuildPalm()
        {
            var c=new Canvas(49,65);
            for(int y=0;y<65;y++)for(int x=0;x<49;x++)
            {
                bool body=x>=12 && x<=36 && y>=10 && y<=36;
                bool wrist=x>=17 && x<=31 && y>=3 && y<16;
                bool thumb=(x-9)*(x-9)/36f+(y-26)*(y-26)/100f<1;
                bool finger=false;
                for(int i=0;i<4;i++)
                {
                    int cx=15+i*6,top=i==1?60:i==2?57:i==0?53:48;
                    if(Mathf.Abs(x-cx)<=2 && y>=32 && y<=top)finger=true;
                }
                if(!body && !wrist && !thumb && !finger)continue;
                int ci=x%6==0?1:x<19?5:y<20?2:4;
                // Two broad palm creases, not fine noise.
                if(body && ((y==24 && x>18) || (y==31 && x<30)))ci=3;
                c.Px[y*c.W+x]=Ramp[ci];
            }
            return c;
        }
        public static Sprite[] MoonFrames=>_moonFrames??(_moonFrames=new[]{Moon});
        public static Sprite[] PalmFrames=>_palmFrames??(_palmFrames=new[]{
            _palm??(_palm=BuildPalm().ToSprite("hn_open_palm",new Vector2(.5f,.5f)))});
        public static void Shatter(Vector2 at,float px,float radius,int count=6)
        {
            if(!Kit.Visible(at,.5f))return;
            count=Mathf.Clamp(count,3,8);
            for(int i=0;i<count;i++)
            {
                float a=i*Mathf.PI*2/count+.25f;
                Vector2 d=new Vector2(Mathf.Cos(a),Mathf.Sin(a));
                Particle p=Fx.Spawn(Blade(false),at+d*radius*.2f,px*.35f,.45f,FxLayer.Top,22);
                if(p!=null){p.Rot=a*Mathf.Rad2Deg;p.Spin=(i%2==0?1:-1)*120;
                    p.Moving(d*radius*2,0,2).Color(Color.white,Fx.WithA(Rose,.5f),Fx.WithA(Rose,0));}
            }
        }
        internal static Canvas BuildCrescent(int radius)
        {
            radius=Mathf.Clamp(radius,6,26);
            var c=new Canvas(radius*2+3,radius*2+3);int mid=radius+1;
            for(int y=0;y<c.H;y++)for(int x=0;x<c.W;x++)
            {
                float dx=x-mid,dy=y-mid,d=Mathf.Sqrt(dx*dx+dy*dy);
                float cut=Mathf.Sqrt((dx+radius*.4f)*(dx+radius*.4f)+dy*dy);
                if(d>radius || cut<radius*.91f)continue;
                float edge=radius-d,inner=cut-radius*.91f;
                int ci=edge<1?0:inner<1.4f?2:edge<2.5f?5:dy>0?4:3;
                c.Px[y*c.W+x]=Ramp[ci];
            }
            return c;
        }
        internal static Canvas BuildFullMoon()
        {
            var c=new Canvas(81,81);
            for(int y=0;y<81;y++)for(int x=0;x<81;x++)
            {
                float dx=(x-40)/39f,dy=(y-40)/39f,r=dx*dx+dy*dy;
                if(r>1)continue;
                float light=.68f-.17f*dx+.12f*dy;
                // Broad crater clusters, not random speckles: clear even at game scale.
                if((dx+.32f)*(dx+.32f)+(dy-.12f)*(dy-.12f)<.045f ||
                   (dx-.25f)*(dx-.25f)+(dy+.35f)*(dy+.35f)<.025f ||
                   (dx-.38f)*(dx-.38f)+(dy-.35f)*(dy-.35f)<.065f)light-=.24f;
                int ci=r>.94f?1:r>.84f?5:light<.49f?2:light<.66f?3:light<.8f?4:5;
                c.Px[y*81+x]=Ramp[ci];
            }
            return c;
        }
        internal static Canvas BuildStar()
        {
            var c=new Canvas(25,25);
            for(int y=0;y<25;y++)for(int x=0;x<25;x++)
            {
                float dx=Mathf.Abs(x-12),dy=Mathf.Abs(y-12);
                if(dx/12f+dy/3f>1 && dx/3f+dy/12f>1)continue;
                c.Px[y*25+x]=dx+dy<4?Ramp[5]:dx<1 || dy<1?Ramp[4]:Ramp[2];
            }
            return c;
        }
        public static Sprite[] Blade(bool large,float size=1)
        {
            int i=large?2:size<1.5f?0:1;
            return Crescents[i]??(Crescents[i]=new[]{BuildCrescent(i==2?26:i==1?14:9).ToSprite("hn_crescent_"+i,new Vector2(.5f,.5f))});
        }
        public static Sprite Moon=>_moon??(_moon=BuildFullMoon().ToSprite("hn_full_moon",new Vector2(.5f,.5f)));
        public static Sprite Star=>_star??(_star=BuildStar().ToSprite("hn_four_star",new Vector2(.5f,.5f)));
        public static void Glint(Vector2 at,float px,float scale,float life=.18f)
        {
            Fx.Spawn(Star,at,px*scale,life,FxLayer.Top,23)?.Sized(.65f,1.2f)
                .Color(Color.white,Fx.WithA(Color.white,0));
        }
    }
}
