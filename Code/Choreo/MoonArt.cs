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
        public const float FullRadius=33f;
        private static readonly Color32[] Silver={
            new Color32(255,255,247,255),new Color32(237,236,225,255),
            new Color32(211,211,204,255),new Color32(178,180,178,255),
            new Color32(150,157,161,255)
        };
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
                if(d>radius || cut<radius*.91f)
                {
                    if(d>radius && d<radius+1 && cut>radius*.91f)
                        c.Px[y*c.W+x]=new Color32(121,145,239,65);
                    continue;
                }
                float edge=radius-d,inner=cut-radius*.91f;
                float q=inner/Mathf.Max(1,inner+edge);
                Color32 color=edge<1.1f?new Color32(107,123,224,255):
                    q<.26f?new Color32(255,254,211,255):
                    q<.55f?new Color32(255,239,182,255):
                    q<.8f?new Color32(255,206,192,255):new Color32(242,181,207,255);
                float ux=Mathf.Floor(dx/2)*2/radius,uy=Mathf.Floor(dy/2)*2/radius;
                if(edge>1.5f && q>.48f && (Patch(ux,uy,.52f,.48f,.18f,.25f) ||
                    Patch(ux,uy,.76f,-.15f,.14f,.2f)))color=new Color32(190,151,214,255);
                c.Px[y*c.W+x]=color;
            }
            return c;
        }
        internal static Canvas BuildFullMoon()
        {
            var c=new Canvas(81,81);
            for(int y=0;y<81;y++)for(int x=0;x<81;x++)
            {
                float dx=x-40,dy=y-40,d=Mathf.Sqrt(dx*dx+dy*dy);
                if(d>39)continue;
                if(d>FullRadius)
                {
                    byte alpha=(byte)(d<35?120:d<37?65:25);
                    c.Px[y*81+x]=new Color32(130,191,225,alpha);continue;
                }
                float ux=Mathf.Floor(dx/2)*2/FullRadius,uy=Mathf.Floor(dy/2)*2/FullRadius;
                int shade=1;
                if(Patch(ux,uy,-.28f,.4f,.38f,.22f) || Patch(ux,uy,.12f,.13f,.25f,.32f) ||
                   Patch(ux,uy,.45f,-.06f,.24f,.23f) || Patch(ux,uy,-.48f,-.32f,.2f,.28f))shade=3;
                if(Patch(ux,uy,.03f,-.23f,.3f,.17f) || Patch(ux,uy,.42f,.43f,.15f,.14f))shade=2;
                if(Patch(ux,uy,-.42f,-.48f,.11f,.12f))shade=4;
                if(Patch(ux,uy,-.1f,.53f,.25f,.13f) || Patch(ux,uy,.3f,.22f,.32f,.13f))shade=3;
                if(Patch(ux,uy,-.28f,.13f,.17f,.13f) || Patch(ux,uy,.21f,-.1f,.13f,.1f))shade=1;
                if(Patch(ux,uy,-.13f,-.40f,.19f,.10f))shade=2;
                if(Patch(ux,uy,-.47f,.25f,.12f,.08f) || Patch(ux,uy,.53f,-.36f,.11f,.09f))shade=2;
                if(Patch(ux,uy,-.06f,.38f,.11f,.08f) || Patch(ux,uy,.35f,.08f,.09f,.12f))shade=1;
                if(Patch(ux,uy,-.38f,-.22f,.14f,.12f) && !Patch(ux,uy,-.36f,-.23f,.09f,.07f))shade=2;
                if(Patch(ux,uy,.1f,-.12f,.11f,.09f) && !Patch(ux,uy,.1f,-.12f,.06f,.05f))shade=0;
                if(Patch(ux,uy,.18f,-.50f,.2f,.18f) && !Patch(ux,uy,.18f,-.50f,.14f,.12f))shade=0;
                if(d>FullRadius-2.4f)shade=0;
                c.Px[y*81+x]=Silver[shade];
            }
            return c;
        }
        private static bool Patch(float x,float y,float cx,float cy,float rx,float ry)
        {
            float dx=(x-cx)/rx,dy=(y-cy)/ry;
            return dx*dx+dy*dy<1+.12f*Mathf.Sin(x*31+y*17);
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
