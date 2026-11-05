using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;

namespace Sakuga
{
    internal static class LunarGodRules
    {
        public const int MaxActive=6,MaxTargets=24,MaxCandidates=64,DomainPulses=4;
        public const float Warning=.9f,Fall=.65f,DomainDuration=4.4f;
        public static float FallProgress(float age)=>Mathf.Clamp01((age-Warning)/Fall);
        public static float ImpactDamage(int level)=>2.8f+.5f*(Mathf.Clamp(level,1,3)-1);
    }

    internal abstract class LunarGodTech : Technique
    {
        protected abstract bool Domain {get;}
        public override Tier Entry=>Tier.God;
        public override School School=>School.HuyenNguyet;
        public override Role Role=>Role.Area;
        public override Form Forms=>Form.Ki|Form.Area|Form.Charge;
        public override float[] ManaCost=>Domain?new[]{60f,90f,120f}:new[]{70f,100f,130f};
        public override float[] Cooldown=>Domain?new[]{45f,55f,65f}:new[]{35f,45f,55f};
        public override string Icon=>"ui/Icons/sakuga/iconSchoolHuyenNguyet";
        public override Plan Plan(Ctx c,int level)
        {
            if(LunarGodHazard.Active>=LunarGodRules.MaxActive)return null;
            return Choreo.PickArea(c,this,level,Domain?4.5f:5.5f,Domain?4f:12f,
                Domain?1.8f:LunarGodRules.ImpactDamage(level),2);
        }
        public override bool Begin(Actor a,Plan p)
        {
            if(LunarGodHazard.Active>=LunarGodRules.MaxActive)return false;
            var m=ChoreoMove.Make(a,p,this);if(m==null)return false;
            Vector2 at=p.Point;bool domain=Domain;int level=p.Level;
            m.Hue.Core=Color.white;m.Hue.Main=new Color(.85f,.94f,1f);m.Hue.Edge=new Color(.5f,.72f,1f);
            m.Charge(domain?.8f:1.1f,ChargeStyle.Raised,2f);
            m.Do(mm=>
            {
                if(LunarGodHazard.Active<LunarGodRules.MaxActive)
                {
                    Areas.Add(new LunarGodHazard(mm,at,level,domain));
                    Cinema.Title(domain?"VINH DA MINH NGUYET":"THIEN NGUYET TRUY THE",new Color(.8f,.92f,1f),1.5f,at);
                }
            });
            m.Recover(.5f);return m.Start();
        }
    }
    internal sealed class ThienNguyetTruyTheTech : LunarGodTech
    { public override string Id=>"hn_thien_nguyet_truy_the";protected override bool Domain=>false; }
    internal sealed class VinhDaMinhNguyetTech : LunarGodTech
    { public override string Id=>"hn_vinh_da_minh_nguyet";protected override bool Domain=>true; }

    internal sealed class LunarGodHazard : Hazard
    {
        public static int Active {get;private set;}
        private readonly Vector2 _at;private readonly int _level;private readonly bool _domain;
        private readonly string _species;
        private readonly float _radius;private readonly List<Actor> _targets=new List<Actor>(64);
        private Particle _moon;private int _generation,_pulses;private bool _killed,_impacted;
        private float _visualIn;
        private static Sprite _sprite;
        public LunarGodHazard(ChoreoMove m,Vector2 at,int level,bool domain):base(m)
        {
            _at=at;_level=level;_domain=domain;
            _species=A.asset?.id;
            _radius=domain?4.5f:5.5f;Active++;
        }
        private static Sprite MoonSprite()
        {
            if(_sprite!=null)return _sprite;
            return _sprite=BuildMoonCanvas().ToSprite("huyen_nguyet_moon",new Vector2(.5f,.5f));
        }
        internal static Canvas BuildMoonCanvas()
        {
            var c=new Canvas(81,81);
            for(int y=0;y<81;y++)for(int x=0;x<81;x++)
            {
                float dx=(x-40)/39f,dy=(y-40)/39f,r=dx*dx+dy*dy;
                if(r>1)continue;
                float light=Mathf.Clamp01(.68f-.17f*dx+.12f*dy);
                if((dx+.32f)*(dx+.32f)+(dy-.12f)*(dy-.12f)<.045f ||
                   (dx-.25f)*(dx-.25f)+(dy+.35f)*(dy+.35f)<.025f)light-=.18f;
                if(r>.9f)light=.95f;
                c.Set(x,y,light,1);
            }
            return c;
        }
        public override bool Tick(float dt)
        {
            if(!OwnerOk || A.asset?.id!=_species)return false;
            Age+=dt;
            float k=LunarGodRules.FallProgress(Age);
            Vector2 body=_at+new Vector2(_domain?0:5f*(1-k),_domain?7f:16f*(1-k));
            bool visible=Kit.Visible(body,.5f)||Kit.Visible(_at,.5f);
            if(visible)
            {
                if(_moon==null || !_moon.Alive || _moon.Gen!=_generation)
                {
                    _moon=Fx.Spawn(MoonSprite(),body,(_domain?2.4f:3.5f)/39f,1,FxLayer.Top,23);
                    if(_moon!=null){_moon.Held=true;_generation=_moon.Gen;}
                }
                if(_moon!=null && _moon.Gen==_generation){_moon.Pos=body;_moon.R.color=new Color(.85f,.94f,1f);}
                _visualIn-=dt;
                if(_visualIn<=0)
                {
                    _visualIn=.1f;
                    Fx.Ring(_at,Px,.18f,_radius/Px,_radius/Px,1,1,FxLayer.Ground,5,Art.Ground,1,
                        Fx.WithA(Hue.Main,.6f),Fx.WithA(Hue.Edge,0));
                    if(!_domain && Age>LunarGodRules.Warning)
                        Kit.Line(body,body+new Vector2(1,3),Px,.15f,2,FxLayer.Top,21,Fx.WithA(Color.white,.5f),Fx.WithA(Hue.Edge,0));
                }
            }
            else ReleaseMoon();
            if(_domain)
            {
                while(_pulses<LunarGodRules.DomainPulses && Age>=.4f+_pulses)
                {
                    _pulses++;HitIds.Clear();HitArea(.35f+.05f*_level,Hurt.Reel,.4f);
                    int heal=(int)(A.getMaxHealth()*.01f);
                    if(heal>0 && (A.current_position-_at).sqrMagnitude<=_radius*_radius)A.restoreHealth(heal);
                }
                return Age<LunarGodRules.DomainDuration;
            }
            if(!_impacted && k>=1)
            {
                _impacted=true;ReleaseMoon();
                HitArea(LunarGodRules.ImpactDamage(_level),Hurt.Knockdown,3);
                if(visible)
                {
                    Fx.Ring(_at,Px,.6f,2,_radius/Px*2,3,1,FxLayer.Ground,8,Art.Ground,2,
                        Color.white,Fx.WithA(Hue.Edge,0));
                    Kit.Flash(_at,Px,24,.12f,Color.white);Kit.Sparks(_at,Px,12,65,90,Hue.Edge);
                }
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce",Combat.TileAt(_at));
                return false;
            }
            return Age<3;
        }
        private void HitArea(float mult,Hurt hurt,float push)
        {
            _targets.Clear();Choreo.Foes(A,_at,_radius,_targets);
            for(int i=0;i<_targets.Count && i<LunarGodRules.MaxCandidates && HitIds.Count<LunarGodRules.MaxTargets;i++)
                Strike(_targets[i],mult,hurt,_at,push);
            _targets.Clear();
        }
        private void ReleaseMoon()
        {
            if(_moon!=null && _moon.Alive && _moon.Gen==_generation)_moon.Kill();
            _moon=null;
        }
        public override void Kill()
        {
            ReleaseMoon();_targets.Clear();
            if(!_killed){_killed=true;Active=Mathf.Max(0,Active-1);}
        }
    }
    internal static class LunarGodTexts
    {
        [SakugaInit]
        internal static void Init()
        {
            foreach(string lang in new[]{"en","cz","vi"})
            {
                LM.Add(lang,"trait_sakuga_hn_thien_nguyet_truy_the","Thiên Nguyệt Trụy Thế");
                LM.Add(lang,"trait_sakuga_hn_thien_nguyet_truy_the_info","Gọi mặt trăng giáng xuống vùng đã đánh dấu: cảnh báo 0,9 giây, rơi 0,65 giây rồi phát xung kích. Bán kính 5,5 ô, tối đa 24 địch; không phá đất/nhà. Tụ lực có thể bị ngắt.");
                LM.Add(lang,"trait_sakuga_hn_vinh_da_minh_nguyet","Vĩnh Dạ Minh Nguyệt");
                LM.Add(lang,"trait_sakuga_hn_vinh_da_minh_nguyet_info","Mặt trăng chiếu một vùng cố định 4,4 giây, công kích 4 nhịp. Chủ đứng trong vùng hồi 1% máu mỗi nhịp, tối đa 4%; không hồi đồng minh. Không phá địa hình.");
            }
        }
    }
}
