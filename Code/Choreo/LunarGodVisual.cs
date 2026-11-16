using UnityEngine;

namespace Sakuga
{
    // Visual-only companion to native Choreo. No targets, damage, mana or AI gates.
    internal sealed class LunarGodVisual
    {
        private readonly Particle[] _parts=new Particle[9];
        private readonly int[] _gen=new int[9];
        private readonly int _kind;
        private bool _charged;
        private float _release=-1;
        internal static int Kind(string id)
        {
            switch(id)
            {
                case "hn_cuu_nguyet_lien_chau":return 1;
                case "hn_nguyet_than_cu_chuong":return 2;
                case "hn_thien_mac_nguyet_nhan":return 3;
                case "hn_nhat_nguyet_khai_thien":return 4;
                default:return 0;
            }
        }
        internal LunarGodVisual(int kind){_kind=kind;}
        internal static Vector2 HaloOffset(int i)
        {
            float a=(20+i*17.5f)*Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a)*3.2f,Mathf.Sin(a)*2.1f+1);
        }
        internal void Tick(ChoreoMove m,float dt)
        {
            if(m.Finished || m.Ended){Kill();return;}
            if(m.Charging)_charged=true;
            else if(_charged && _release<0)_release=0;
            if(_release>=0)_release+=dt;
            Vector2 center=Choreo.BodyAt(m.A,.65f);
            if(!Kit.Visible(center,.5f)){Kill();return;}
            int n=_kind==1?9:_kind==3?5:1;
            for(int i=0;i<n;i++)
            {
                bool gone=_release>=0 && (_kind==1?_release>=i*.12f:_release>.15f);
                if(gone){Drop(i);continue;}
                Vector2 off=_kind==1?HaloOffset(i):_kind==3?new Vector2((i-2)*1.5f,4.5f):new Vector2(0,1.8f);
                Vector2 at=center+off*m.Kk;
                if(_parts[i]==null || !_parts[i].Alive || _parts[i].Gen!=_gen[i])
                {
                    Sprite sprite=_kind==2?MoonArt.PalmFrames[0]:_kind==3?MoonArt.Blade(false)[0]:MoonArt.Moon;
                    float scale=_kind==1?.65f/39:_kind==2?m.Px*.6f:_kind==3?m.Px*.8f:2.1f/39;
                    _parts[i]=Fx.Spawn(sprite,at,scale*m.Kk,1,FxLayer.Top,18);
                    if(_parts[i]!=null){_parts[i].Held=true;_gen[i]=_parts[i].Gen;}
                }
                Particle p=_parts[i];
                if(p!=null && p.Gen==_gen[i])
                {
                    p.Pos=at;p.R.color=Fx.WithA(Color.white,_release<0?.45f+.55f*m.BeatK:1);
                    if(_kind==3)p.Rot=-90;
                }
            }
        }
        private void Drop(int i)
        {
            Particle p=_parts[i];
            if(p!=null && p.Alive && p.Gen==_gen[i])p.Kill();
            _parts[i]=null;
        }
        internal void Kill(){for(int i=0;i<9;i++)Drop(i);}
    }
}
