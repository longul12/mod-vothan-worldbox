using System;
using NeoModLoader.General;
using UnityEngine;

namespace Sakuga
{
    // Optional API used by CWP. No compile-time dependency on CWP or copied AI loop.
    internal static class MoonSwordApi
    {
        public const string Source="cwp_relic_tongtian_sword", Id="hn_nguyet_khuyet";
        private const string ReadyKey="sakuga.hn.moon_ready.v1", Migrated="sakuga.hn.moon_migrated.v1";
        public static bool IsAvailable()=>Techniques.Get(Id)!=null && Techniques.Trait(Techniques.Get(Id))!=null;
        public static bool HasSource(Actor a)=>a?.data!=null && a.isAlive() && a.hasTrait(Source);
        public static int GrantedLevel(bool source,int learned)=>source?Math.Max(1,learned):learned;
        public static float Multiplier(int level)=>level<=1?1.4f:level==2?1.7f:2f;
        public static float Cooldown(int level)=>level<=1?10f:level==2?12f:14f;
        public static void Reconcile(Actor a)
        {
            if(!HasSource(a))return;
            a.data.get(Migrated,out bool imported,false);
            if(!imported)
            {
                a.data.get("cwp.moon_sword.cooldown.v1",out float remaining,0f);
                if(float.IsNaN(remaining)||float.IsInfinity(remaining))remaining=10;
                a.data.get(ReadyKey,out float existing,0f);
                if(float.IsNaN(existing)||float.IsInfinity(existing))existing=0;
                a.data.set(ReadyKey,Mathf.Max(existing,(float)Fighter.Now+Mathf.Clamp(remaining,0,10)));
                a.data.set(Migrated,true);
            }
            if(!Fighters.TryGet(a,out _))Fighters.Touch(a);
        }
        public static bool Ready(Actor a)
        {
            if(a?.data==null)return false;
            a.data.get(ReadyKey,out float at,0f);
            return !float.IsNaN(at) && !float.IsInfinity(at) && Fighter.Now>=at;
        }
        public static void Mark(Actor a,int level)
        {
            a.data.set(ReadyKey,(float)Fighter.Now+Cooldown(level));
            a.data.set(Migrated,true);
        }
    }

    internal sealed class NguyetKhuyetTech : Technique
    {
        public override string Id=>MoonSwordApi.Id;
        public override Tier Entry=>Tier.Master;
        public override School School=>School.HuyenNguyet;
        public override Role Role=>Role.Line;
        public override Form Forms=>Form.Blade|Form.Ki|Form.Projectile|Form.Line|Form.Charge;
        public override float[] ManaCost=>new[]{24f,36f,52f};
        public override float[] Cooldown=>new[]{10f,12f,14f};
        public override string Icon=>"ui/Icons/sakuga/iconHnNguyetKhuyet";
        public override bool Accepts(Link link)=>link==Link.Launched || link==Link.Scattered || link==Link.Thrown;
        public override Plan Plan(Ctx c,int level)
        {
            if(c.HpPct<0.15f || !MoonSwordApi.Ready(c.Self))return null;
            return Choreo.PickLine(c,this,level,12f,2.2f,MoonSwordApi.Multiplier(level),1,false);
        }
        public override bool Begin(Actor a,Plan p)
        {
            if(!MoonSwordApi.Ready(a) || Combat.HpPct(a)<.15f)return false;
            ChoreoMove m=ChoreoMove.Make(a,p,this);
            if(m==null)return false;
            // Explicit palette keeps this school's signature white/silver even at high tiers.
            m.Hue=Hue.For(Tiers.Of(a),School.HuyenNguyet,p.Level);
            m.Hue.Core=Color.white;m.Hue.Main=new Color(.92f,.97f,1f);m.Hue.Edge=new Color(.65f,.84f,1f);
            m.Face(p.Dir);
            m.Charge(.3f,ChargeStyle.Palms,1.6f);
            m.Shoot(new ShotSpec {Kind=ShotKind.SwordWave,Speed=16,Range=12,Size=2.2f,
                Mult=MoonSwordApi.Multiplier(p.Level),Pierce=7,Crater=0,Area=0,Fire=false,
                PierceDecay=.9f,PierceFloor=.65f,LunarCrescent=true,Push=1.4f,Element=Element.School});
            m.Recover(.25f);
            if(!m.Start())return false;
            // Native techniques consume cooldown on starting, including a subsequently broken charge.
            MoonSwordApi.Mark(a,p.Level);
            return true;
        }
        [SakugaInit]
        internal static void Texts()
        {
            foreach(string lang in new[]{"en","cz","vi"})
            {
                LM.Add(lang,"trait_sakuga_"+MoonSwordApi.Id,"Nguyệt Khuyết");
                LM.Add(lang,"trait_sakuga_"+MoonSwordApi.Id+"_info","Tụ lực phóng vầng trăng khuyết lớn xuyên tối đa 8 địch, sát thương giảm 10% mỗi lần xuyên, sàn 65%. Cấp I–III: 140/170/200%, hồi tối thiểu 10/12/14 giây thế giới; cần 15% máu. Có thể bị ngắt, né hoặc phản đòn. Thái Hư Thiên Kiếm cấp quyền ít nhất cấp I; không cộng thành hai chiêu.");
                LM.Add(lang,"sakuga_title_"+MoonSwordApi.Id,"NGUYET KHUYET");
            }
        }
    }
}
