using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// What a fighter sees when it looks around: its enemies and allies nearby (nearest first),
    /// its target, its chain state, and quick questions about the battlefield (is there a crowd,
    /// a line, a lone straggler, something flying?). Built once per think and shared by every
    /// technique's Plan, so planning stays cheap.
    /// </summary>
    internal sealed class Ctx
    {
        public Fighter F;
        public Actor Self;
        public Tier Tier;
        public TierInfo Info;
        public Vector2 Pos;
        /// <summary>What one technique blow of x1 really deals (damage stat x boosts x the tier's Might x the power setting).</summary>
        public float D;
        /// <summary>The fighter's ordinary blow (no Might): what a technique has to beat.</summary>
        public float Raw;
        public float HpPct;
        /// <summary>Its current attack target, if that is a hostile unit.</summary>
        public Actor Target;
        /// <summary>Hostile units within its awareness, nearest first (flying ones included).</summary>
        public readonly List<Actor> Enemies = new List<Actor>();
        /// <summary>Distances matching Enemies.</summary>
        public readonly List<float> EnemyDist = new List<float>();
        /// <summary>Allied units within its awareness, nearest first.</summary>
        public readonly List<Actor> Allies = new List<Actor>();
        public bool Linked;
        public Link Link;
        public Actor LinkTarget;
        /// <summary>Planned by a god power or a dev command: relax "is it worth it" gates, just find a way.</summary>
        public bool Forced;
        public Actor ForceTarget;
        public Vector2? ForcePoint;
        public float Awareness;

        // ---- the situation (Dynamics): who is around, how dangerous, what the foe is doing
        /// <summary>Its fighting personality.</summary>
        public Persona Persona;
        /// <summary>Hostile units within 2 and within 4 tiles.</summary>
        public int Near2, Near4;
        /// <summary>From how many sides (0..4: left, right, above, below) the enemies within 3 tiles come.</summary>
        public int Sides;
        /// <summary>Allies within 4 tiles.</summary>
        public int AlliesClose;

        private readonly List<KeyValuePair<float, Actor>> _sort = new List<KeyValuePair<float, Actor>>();
        private readonly List<KeyValuePair<float, Actor>> _allySort = new List<KeyValuePair<float, Actor>>();
        private readonly List<Actor> _scan = new List<Actor>();

        // lazily worked out once per look
        private float _selfValue = -1f, _around = -1f;
        private int _terrain = -1;
        private Actor _stateOf;
        private FoeState _state;

        public void Build(Fighter f)
        {
            F = f;
            Self = f.A;
            Tier = f.Tier;
            Info = f.Info;
            Pos = Self.current_position;
            Persona = f.Persona;
            Near2 = Near4 = Sides = AlliesClose = 0;
            _selfValue = -1f;
            _around = -1f;
            _terrain = -1;
            _stateOf = null;
            Raw = Mathf.Max(4f, Self.stats["damage"]) * Fighters.DamageMult(Self);
            D = Raw * (Info != null ? Info.Might : 1f) * Settings.Power;
            HpPct = Combat.HpPct(Self);
            Linked = f.Linked;
            Link = Linked ? f.Link : Link.None;
            // (the same unit still, still a foe, not a body a blow already took or a meteor carries off)
            Actor lt = Linked ? f.LinkTargetNow : null;
            LinkTarget = lt != null && Combat.IsFoe(Self, lt) && !MeteorSlam.IsDoomed(lt) ? lt : null;
            Forced = false;
            ForceTarget = null;
            ForcePoint = null;
            Awareness = Info != null ? Info.Awareness : 6f;
            Enemies.Clear();
            EnemyDist.Clear();
            Allies.Clear();
            Target = null;
            if (Self.has_attack_target && Self.attack_target != null && Self.attack_target.isActor() && Combat.IsFoe(Self, Self.attack_target.a)
                && !MeteorSlam.IsDoomed(Self.attack_target.a))
            {
                Target = Self.attack_target.a;
            }
            WorldTile tile = Self.current_tile;
            if (tile == null)
            {
                return;
            }
            _scan.Clear();
            int chunks = Awareness > 12f ? 2 : 1;
            foreach (Actor u in Finder.getUnitsFromChunk(tile, chunks))
            {
                _scan.Add(u);
            }
            float r2 = Awareness * Awareness;
            _sort.Clear();
            List<KeyValuePair<float, Actor>> allies = _allySort;
            allies.Clear();
            foreach (Actor u in _scan)
            {
                if (u == null || u == Self || !u.isAlive() || !u.hasHealth() || MeteorSlam.IsDoomed(u))
                {
                    continue;   // (a body a lethal blow already took, or one a meteor is carrying to its death)
                }
                float d2 = (u.current_position - Pos).sqrMagnitude;
                if (d2 > r2)
                {
                    continue;
                }
                if (Combat.IsFoe(Self, u))
                {
                    _sort.Add(new KeyValuePair<float, Actor>(d2, u));
                }
                else if (Combat.IsAlly(Self, u))
                {
                    allies.Add(new KeyValuePair<float, Actor>(d2, u));
                }
            }
            _sort.Sort((x, y) => x.Key.CompareTo(y.Key));
            allies.Sort((x, y) => x.Key.CompareTo(y.Key));
            int sideMask = 0;
            for (int i = 0; i < _sort.Count && i < 28; i++)
            {
                Actor u = _sort[i].Value;
                float d = Mathf.Sqrt(_sort[i].Key);
                Enemies.Add(u);
                EnemyDist.Add(d);
                if (d <= 4f)
                {
                    Near4++;
                    if (d <= 2f)
                    {
                        Near2++;
                    }
                    if (d <= 3f)
                    {
                        Vector2 rel = u.current_position - Pos;
                        sideMask |= Mathf.Abs(rel.x) >= Mathf.Abs(rel.y) ? (rel.x >= 0f ? 1 : 2) : (rel.y >= 0f ? 4 : 8);
                    }
                }
            }
            Sides = (sideMask & 1) + ((sideMask >> 1) & 1) + ((sideMask >> 2) & 1) + ((sideMask >> 3) & 1);
            for (int i = 0; i < allies.Count && i < 16; i++)
            {
                Allies.Add(allies[i].Value);
                if (allies[i].Key <= 16f)
                {
                    AlliesClose++;
                }
            }
            if (Target == null && Enemies.Count > 0 && Self.isFighting())
            {
                Target = Enemies[0];
            }
        }

        // ------------------------------------------------------------ helpers --

        public float Dist(Actor v)
        {
            return Vector2.Distance(Pos, v.current_position);
        }

        /// <summary>May it use techniques on this one (class limits; 'mult' is 0.5 when punching above its class)?</summary>
        public bool Allowed(Actor v, out float mult)
        {
            return Combat.Allowed(Self, v, out mult);
        }

        public bool Allowed(Actor v)
        {
            return Combat.Allowed(Self, v, out _);
        }

        /// <summary>Within reach of a melee technique (touching distance).</summary>
        public bool Adjacent(Actor v, float extra = 0f)
        {
            float reach = 1.25f + (Self.stats["size"] + v.stats["size"]) * 0.5f + extra;
            return Dist(v) <= reach;
        }

        /// <summary>Damage a blow of 'mult' would do to v after armor.</summary>
        public float Expected(Actor v, float mult)
        {
            float armor = Mathf.Min(v.stats["armor"], 95f);
            return D * mult * Mathf.Max(0.05f, 1f - armor / 100f) * Duels.TechShare(Self, v);
        }

        /// <summary>
        /// What dealing 'damage' to v is worth: the damage that actually counts (no overkill),
        /// scaled by how dangerous v is, plus a bonus if it dies (more if it is tiered).
        /// </summary>
        public float Value(Actor v, float damage, float classMult = 1f)
        {
            damage *= classMult;
            float hp = v.getHealth();
            float worth = Mathf.Min(damage, hp);
            float danger = Mathf.Clamp(Tiers.Power(v), 0.3f, 6f);
            float value = worth * (0.6f + 0.4f * danger);
            if (damage >= hp)
            {
                value += Tiers.Of(v) != Tier.None ? 60f : 30f;
            }
            return value;
        }

        /// <summary>Allowed, grounded enemies within r of p.</summary>
        public int CountNear(Vector2 p, float r, List<Actor> into = null, bool groundOnly = true)
        {
            int n = 0;
            float r2 = r * r;
            foreach (Actor v in Enemies)
            {
                if (groundOnly && !Combat.Grounded(v))
                {
                    continue;
                }
                if (Moves.Locked(v) || (v.current_position - p).sqrMagnitude > r2 || !Allowed(v))
                {
                    continue;
                }
                n++;
                into?.Add(v);
            }
            return n;
        }

        public int AlliesNear(Vector2 p, float r)
        {
            int n = 0;
            float r2 = r * r;
            foreach (Actor v in Allies)
            {
                if ((v.current_position - p).sqrMagnitude <= r2)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>No other hostile unit near v (it's alone: an execution).</summary>
        public bool Isolated(Actor v, float r = 3f)
        {
            float r2 = r * r;
            foreach (Actor u in Enemies)
            {
                if (u != v && (u.current_position - v.current_position).sqrMagnitude <= r2)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The best centre for an area technique: the enemy (or point between enemies) with the most
        /// allowed, grounded enemies within 'radius', between minRange and maxRange tiles away.
        /// </summary>
        public Vector2 Cluster(float radius, float minRange, float maxRange, out int count, out Actor center)
        {
            count = 0;
            center = null;
            Vector2 best = Pos;
            for (int i = 0; i < Enemies.Count; i++)
            {
                Actor v = Enemies[i];
                float d = EnemyDist[i];
                if (d < minRange || d > maxRange || !Combat.Grounded(v) || !Allowed(v) || Moves.Locked(v))
                {
                    continue;
                }
                // centre on the local group's middle, not on one unit's toes
                Vector2 sum = Vector2.zero;
                int k = 0;
                foreach (Actor u in Enemies)
                {
                    if (Combat.Grounded(u) && (u.current_position - v.current_position).sqrMagnitude <= radius * radius)
                    {
                        sum += u.current_position;
                        k++;
                    }
                }
                Vector2 c = k > 0 ? sum / k : v.current_position;
                int n = CountNear(c, radius);
                if (n > count)
                {
                    count = n;
                    best = c;
                    center = v;
                }
            }
            return best;
        }

        /// <summary>Allowed grounded enemies within 'width' of the segment from Pos along dir for len tiles.</summary>
        public int OnRay(Vector2 dir, float len, float width, List<Actor> into = null)
        {
            int n = 0;
            dir = dir.normalized;
            foreach (Actor v in Enemies)
            {
                if (!Combat.Grounded(v) || Moves.Locked(v) || !Allowed(v))
                {
                    continue;
                }
                Vector2 rel = v.current_position - Pos;
                float along = Vector2.Dot(rel, dir);
                if (along < 0.3f || along > len)
                {
                    continue;
                }
                float off = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
                if (off <= width + v.stats["size"] * 0.5f)
                {
                    n++;
                    into?.Add(v);
                }
            }
            return n;
        }

        /// <summary>The direction (towards one of the enemies) that lines up the most of them.</summary>
        public Vector2 BestRay(float len, float width, out int count)
        {
            count = 0;
            Vector2 best = Target != null ? (Target.current_position - Pos).normalized : Vector2.right;
            for (int i = 0; i < Enemies.Count && i < 16; i++)
            {
                if (EnemyDist[i] > len || EnemyDist[i] < 0.3f)
                {
                    continue;
                }
                Vector2 dir = (Enemies[i].current_position - Pos).normalized;
                int n = OnRay(dir, len, width);
                if (n > count)
                {
                    count = n;
                    best = dir;
                }
            }
            return best;
        }

        /// <summary>Hostile units right next to it.</summary>
        public int Surrounding(float r = 1.6f)
        {
            int n = 0;
            for (int i = 0; i < Enemies.Count && EnemyDist[i] <= r; i++)
            {
                n++;
            }
            return n;
        }

        /// <summary>The nearest flying or airborne enemy it may fight, if any.</summary>
        public Actor Flier(float maxDist)
        {
            for (int i = 0; i < Enemies.Count; i++)
            {
                Actor v = Enemies[i];
                if (EnemyDist[i] <= maxDist && (v.isFlying() || v.position_height > 0.8f) && Allowed(v))
                {
                    return v;
                }
            }
            return null;
        }

        /// <summary>The heaviest enemy it is allowed to fight (for single-target finishers).</summary>
        public Actor Biggest(float maxDist)
        {
            Actor best = null;
            float bestPower = -1f;
            for (int i = 0; i < Enemies.Count; i++)
            {
                Actor v = Enemies[i];
                if (EnemyDist[i] > maxDist || !Allowed(v))
                {
                    continue;
                }
                float p = Tiers.Power(v);
                if (p > bestPower)
                {
                    bestPower = p;
                    best = v;
                }
            }
            return best;
        }

        /// <summary>
        /// Is 'v' on guard against an ordinary single blow? 1 = it knows a reactive technique that could
        /// meet one (Still Water, Mirage Step...) ready and affordable right now, -1 = it knows one but
        /// just spent it (punish now!), 0 = none. Single-target openers weigh it: x0.6 into a ready
        /// guard, a bonus right after one was used. Guard(v, forms) asks about a particular attack.
        /// </summary>
        public int Guard(Actor v)
        {
            return Guard(v, Form.Strike | Form.Dash);
        }

        /// <summary>Is 'v' on guard against an attack of these forms (only answers whose Answers meet them count)?</summary>
        public int Guard(Actor v, Form incoming)
        {
            if (v == null || !Fighters.TryGet(v, out Fighter f))
            {
                return 0;
            }
            int result = 0;
            bool free = Moves.CanPerform(v);
            IReadOnlyList<Technique> list = Techniques.Reactives;
            for (int i = 0; i < list.Count; i++)
            {
                Technique t = list[i];
                if (t.Answers != Form.None && (t.Answers & incoming) == 0)
                {
                    continue;   // (an interrupt jab is no guard against a sword)
                }
                int level = Techniques.Level(v, t, f.Tier);
                if (level <= 0)
                {
                    continue;
                }
                if (!free)
                {
                    return -1;   // it knows an answer to this but can't use one now
                }
                if (f.Ready(t) && Mana.Has(v, t, level, false))
                {
                    return 1;
                }
                result = -1;
            }
            return result;
        }

        /// <summary>The target to use for a single-target technique: the link target, the forced one, or its own.</summary>
        public Actor Focus => ForceTarget ?? LinkTarget ?? Target;

        // ------------------------------------------------------------ dynamics --

        /// <summary>Three or more enemies within 2 tiles, or four within 4 from three sides or more.</summary>
        public bool Surrounded => Near2 >= 3 || Near4 >= 4 && Sides >= 3;

        /// <summary>Clearly fewer of us than of them close by (or in sight).</summary>
        public bool Outnumbered => Near4 >= AlliesClose + 3 || Enemies.Count >= 2 * (Allies.Count + 1) + 2;

        /// <summary>Clearly more of us than of them close by.</summary>
        public bool Outnumbering => AlliesClose >= Near4 + 2 && Near4 > 0;

        /// <summary>At 30% of its health or less (its last stand: bigger answers, or a way out).</summary>
        public bool Desperate => HpPct < 0.3f;

        /// <summary>How dangerous 'v' is to this fighter (Threat: 1 an equal, 0.2 a nuisance, 3 a mortal danger).</summary>
        public float Threat(Actor v)
        {
            if (v == null)
            {
                return ThreatAround;
            }
            if (_selfValue < 0f)
            {
                _selfValue = Mathf.Max(1f, global::Sakuga.Threat.Value(Self));
            }
            return global::Sakuga.Threat.Value(v) / _selfValue;
        }

        /// <summary>The threat of every enemy within 8 tiles together (an army of goblins adds up).</summary>
        public float ThreatAround
        {
            get
            {
                if (_around < 0f)
                {
                    float sum = 0f;
                    for (int i = 0; i < Enemies.Count && i < 16 && EnemyDist[i] <= 8f; i++)
                    {
                        sum += Threat(Enemies[i]);
                    }
                    _around = sum;
                }
                return _around;
            }
        }

        /// <summary>
        /// How fitting a technique of Scale 's' is against 'v' (null: everyone around): 1 in proportion,
        /// down to 0.04 for an ultimate on a chicken (Threat.Fit).
        /// </summary>
        public float ScaleFit(Scale s, Actor v = null)
        {
            return global::Sakuga.Threat.Fit(s, global::Sakuga.Threat.Ideal(v != null ? Threat(v) : ThreatAround, HpPct));
        }

        /// <summary>How far 'v' strikes from (its attack range in tiles: an archer's or a mage's is long: close in or deflect).</summary>
        public float FoeRange(Actor v)
        {
            return v != null ? v.getAttackRange() : 0f;
        }

        /// <summary>What kind of creature 'v' is (Preys.Of).</summary>
        public Prey PreyOf(Actor v)
        {
            return Preys.Of(v);
        }

        /// <summary>1 for a technique made for no one in particular; 1.5 for one made for this creature; 0.7 otherwise.</summary>
        public float PreyFit(Prey made, Actor v)
        {
            if (made == Prey.None)
            {
                return 1f;
            }
            return v != null && (Preys.Of(v) & made) != 0 ? 1.5f : 0.7f;
        }

        /// <summary>What 'v' is doing and how it stands right now (charging, guarding, airborne, down...).</summary>
        public FoeState State(Actor v)
        {
            if (v == null)
            {
                return default;
            }
            if (v != _stateOf)
            {
                _state = FoeState.Of(v, Self);
                _stateOf = v;
            }
            return _state;
        }

        /// <summary>What this fighter remembers of 'v' (its habits, what worked on it); null if nothing.</summary>
        public FoeNotes Notes(Actor v)
        {
            return F.Recall(v);
        }

        /// <summary>Water within 2 tiles (a crack stops at it, a dash can't cross it).</summary>
        public bool WaterNear => (Terrain() & 1) != 0;

        /// <summary>Mountains, walls or lava within 2 tiles.</summary>
        public bool CliffNear => (Terrain() & 2) != 0;

        /// <summary>Buildings within 2 tiles.</summary>
        public bool BuildingsNear => (Terrain() & 4) != 0;

        /// <summary>Fire within 2 tiles.</summary>
        public bool Burning => (Terrain() & 8) != 0;

        /// <summary>Standing in (shallow) water itself.</summary>
        public bool InWater => Self.isInLiquid();

        private int Terrain()
        {
            if (_terrain >= 0)
            {
                return _terrain;
            }
            int m = 0;
            WorldTile c = Self.current_tile;
            if (c != null && World.world != null)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        WorldTile t = World.world.GetTile(c.x + dx, c.y + dy);
                        if (t == null || t.Type == null)
                        {
                            continue;
                        }
                        if (t.Type.liquid)
                        {
                            m |= 1;
                        }
                        if (t.Type.block || t.Type.lava)
                        {
                            m |= 2;
                        }
                        if (t.hasBuilding())
                        {
                            m |= 4;
                        }
                        if (t.isOnFire())
                        {
                            m |= 8;
                        }
                    }
                }
            }
            _terrain = m;
            return m;
        }
    }

    /// <summary>
    /// The tactical mind. A few times a second (faster for higher tiers) every fighter in a battle
    /// looks around and asks each technique it knows "what could you do here, and how good is it?";
    /// the best answer that beats an ordinary blow is played. High tiers judge well, notice more,
    /// chain techniques into combos, and don't waste a Meteor Slam on two peasants; Novices are eager
    /// and clumsy. Variety is part of it: a fighter avoids repeating itself, and a battlefield
    /// avoids showing the same technique over and over in one spot.
    /// </summary>
    internal static class Brain
    {
        private static readonly Ctx _ctx = new Ctx();

        /// <summary>Techniques started since the game started, by id (dev statistics).</summary>
        internal static readonly Dictionary<string, int> Started = new Dictionary<string, int>();

        public static void Update(float dt)
        {
            Fighters.Maintain(Time.unscaledDeltaTime);
            if (dt <= 0f || World.world == null)
            {
                return;
            }
            double now = Fighter.Now;
            IReadOnlyList<Fighter> list = Fighters.All;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (i >= list.Count)
                {
                    continue;
                }
                Fighter f = list[i];
                if (!f.Valid)
                {
                    continue;
                }
                try
                {
                    Step(f, dt, now);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] brain: " + e);
                    f.ThinkIn = 2f;
                }
            }
        }

        private static void Step(Fighter f, float dt, double now)
        {
            Actor a = f.A;
            if (a.isFighting())
            {
                f.LastCombat = now;
                if (f.CombatSince < 0)
                {
                    f.CombatSince = now;
                    f.LowestHp = 1f;
                    f.SecondWindUsed = false;
                }
                f.LowestHp = Mathf.Min(f.LowestHp, Combat.HpPct(a));
            }
            else if (!f.InCombat && f.CombatSince >= 0)
            {
                double since = f.CombatSince;
                f.CombatSince = -1;
                f.Heat = 0f;
                f.Chain = 0;
                Progression.BattleOver(f, since);
            }
            Mana.Regen(f, dt);
            Hooks.FighterTick?.Invoke(f, dt);
            if (f.Link != Link.None && !f.Linked)
            {
                f.Link = Link.None;
                f.LinkTarget = null;
                f.Chain = 0;
                f.Heat = 0f;
            }
            // a unit in a light move (a flow strike) may think: a technique takes over at once
            // (never one gathering power: it is committed to its own wind-up)
            if (Moves.Committed(a) || Director.InScene(a) || Primordials.Any && Awakenings.Waiting(a))
            {
                return;
            }
            f.ThinkIn -= dt;
            f.LinkThinkIn -= dt;
            // a chain window is watched closely, but every few frames, not every one (big wars)
            if (f.Linked ? f.LinkThinkIn > 0f : f.ThinkIn > 0f)
            {
                return;
            }
            f.ThinkIn = (f.Info != null ? f.Info.Think : 0.9f) * UnityEngine.Random.Range(0.8f, 1.2f);
            f.LinkThinkIn = 0.05f;
            // Flow tiers keep fighting between targets: they look for the next enemy themselves
            if (!a.isFighting() && !f.Linked && !(f.Info != null && f.Info.Flow && f.InCombat))
            {
                return;
            }
            // a unit still in the air after a move may chain an aerial follow-up (Air Meteor)
            bool airLink = f.Linked && f.Link == Link.SelfAir && MeteorSlam.CanPerformAir(a);
            if (!airLink && !Moves.CanPerform(a))
            {
                return;
            }
            for (int i = 0; i < Hooks.HoldTechniques.Count; i++)
            {
                if (Hooks.HoldTechniques[i](a))
                {
                    return;
                }
            }
            // a budget for huge wars: fighters off screen give way to the ones the player is watching
            int busy = Moves.Count;
            if (busy >= 64 || busy >= 28 && !Fx.OnScreen(a.current_position, 0.15f))
            {
                return;
            }
            Think(f);
        }

        private static void Think(Fighter f)
        {
            Ctx c = _ctx;
            c.Build(f);
            if (c.Enemies.Count == 0)
            {
                if (f.Link != Link.None)
                {
                    // nobody left to chain onto: the window closes as if it expired (its extra damage too)
                    f.Link = Link.None;
                    f.LinkTarget = null;
                    f.Chain = 0;
                    f.Heat = 0f;
                }
                return;
            }
            bool linked = f.Linked;
            double now = Fighter.Now;
            if (!linked)
            {
                // its persona sets its tempo: a swarmer presses, a counter-puncher bides its time;
                // something to answer right now (a foe winding up, an attack aimed at it) cuts the pause short
                Persona per = c.Persona;
                float gap = (f.Info != null ? f.Info.MinGap : 2.5f) / Settings.Frequency * Mathf.Lerp(1.3f, 0.7f, per.Aggression);
                // (a Primordial is never on the back foot: an army's blows keep it parrying, but it takes the
                // initiative back at once)
                bool prim = Primordials.Is(f.A);
                if (prim)
                {
                    gap *= 0.3f;
                }
                bool alerted = Reads.Alerted(f);
                if (now - f.LastEnd < (alerted ? gap * 0.35f : gap))
                {
                    return;
                }
                if (!prim && !alerted && f.Info != null && UnityEngine.Random.value < f.Info.JustAttack * Mathf.Lerp(1.4f, 0.6f, per.Aggression) / Mathf.Max(0.2f, Settings.Frequency))
                {
                    return;
                }
            }
            Plan best = Choose(f, c, linked);
            // the higher the tier, the less an ordinary blow is ever the better choice: Masters and up
            // fight with techniques whenever one is viable
            float bar = f.Info == null ? 1.6f : f.Info.Flow ? 0.15f : f.Tier == Tier.Expert ? 0.8f : f.Tier == Tier.Adept ? 0.9f : 1.0f;
            // (scores count the power setting through c.D, so the bar does too: technique power changes
            // how hard techniques hit, not how often they are used; that is Settings.Frequency's job)
            float baseline = c.Raw * bar * Settings.Power / Mathf.Max(0.2f, Settings.Frequency);
            if (best == null || best.Score < baseline)
            {
                return;   // a chain window stays open until it expires (Step closes it)
            }
            Execute(f, best);
        }

        private static Plan Choose(Fighter f, Ctx c, bool linked)
        {
            Plan best = null;
            float noise = f.Info != null ? f.Info.Noise : 0.3f;
            foreach (Technique t in Techniques.All)
            {
                // reactive techniques plan too (a proactive use, e.g. an evasive retreat) or return null
                if (t.GodAbility || (t.Team && !Settings.TeamTechniques))
                {
                    continue;
                }
                int level = Techniques.Level(f.A, t, f.Tier);
                if (level <= 0 || !f.Ready(t))
                {
                    continue;
                }
                if (linked && !t.Accepts(f.Link))
                {
                    continue;
                }
                if (!Mana.Has(f.A, t, level, linked))
                {
                    continue;
                }
                Plan p;
                try
                {
                    p = t.Plan(c, level);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] plan " + t.Id + ": " + e);
                    continue;
                }
                if (p == null || p.Score <= 0f)
                {
                    continue;
                }
                // (a whole technique is not started on a body that is already dying; a plan aimed at no unit,
                // a retreat or a charge to a point, has no body to be spent: Spent(null) would drop it)
                if (p.AreaRadius <= 0f && p.Target != null && Spent(p.Target))
                {
                    continue;
                }
                // (a team technique whose setup on this enemy just failed in Begin, Crew.Fail: not offered
                // again on every look for a few seconds; the team syncs record it but never asked)
                if (t.Team && p.Target != null && Crew.Failed(t.Id, p.Target))
                {
                    continue;
                }
                p.Tech = t;
                p.Level = level;
                p.Linked = linked;
                // what the fighter reads of its foe, who it is, what it remembers (Dynamics)
                float shape;
                try
                {
                    shape = Tactics.Shape(f, c, t, level, p);
                }
                catch (Exception e)
                {
                    Debug.LogError("[Sakuga] tactics " + t.Id + ": " + e);
                    shape = 1f;
                }
                if (shape <= 0f)
                {
                    continue;   // (a hunter's technique for a creature this is not)
                }
                if (t.PrimordialOnly)
                {
                    // (beside a Primordial even a God is a small threat: its own powers are never "overkill")
                    shape = Mathf.Max(shape, 1f);
                }
                p.Shape = shape;
                p.Score *= shape;
                if (Dev.On)
                {
                    ScoreProbe(f, t, p.Score);
                }
                if (Dev.On && t.Id == "heaven_splitter")
                {
                    LastSplitter = p;
                }
                p.Score *= 1f + UnityEngine.Random.Range(-noise, noise);
                p.Score *= Variety(f, t, c.Pos);
                // (in a duel: what it has already shown counts for less, what it hasn't for more)
                p.Score *= Duels.Freshness(f.A, t);
                p.Score = Divine(f, t, p, p.Score);
                p.Score *= Range(f, c, t, level, p);
                p.Score *= Matchup(t, level, p.Target);
                if (p.AreaRadius > 0f)
                {
                    p.Score *= Director.AreaFree(p.Point, p.AreaRadius, f.A);
                }
                if (linked)
                {
                    p.Score += 40f;
                }
                if (best == null || p.Score > best.Score)
                {
                    best = p;
                }
            }
            if (Dev.On && best != null)
            {
                ProbeChoice(f, best);
            }
            return best;
        }

        /// <summary>Dev (scoreprobe): every candidate plan's score before noise and variety, by technique and the user's tier.</summary>
        internal static readonly Dictionary<string, Vector2> Scores = new Dictionary<string, Vector2>();

        private static void ScoreProbe(Fighter f, Technique t, float score)
        {
            string k = f.Tier + "|" + t.Id;
            Scores.TryGetValue(k, out Vector2 v);
            Scores[k] = v + new Vector2(score, 1f);
        }

        /// <summary>Dev: when Heaven Splitter had a plan this look, who won (by the user's tier).</summary>
        internal static readonly Dictionary<string, int> ChoiceProbe = new Dictionary<string, int>();
        internal static Plan LastSplitter;

        private static void ProbeChoice(Fighter f, Plan best)
        {
            Plan hs = LastSplitter;
            LastSplitter = null;
            if (hs == null)
            {
                return;
            }
            string k = f.Tier + (best == hs ? " won" : " lost to " + best.Tech.Id);
            ChoiceProbe.TryGetValue(k, out int n);
            ChoiceProbe[k] = n + 1;
        }

        /// <summary>
        /// The schools' matchups: what works against a foe of each school, so fights between schools are
        /// fought differently. Against a Ki artist, close the gap (it out-shoots you); against a Phantom,
        /// hit the whole space (it slips single blows) and fast; against an Earth Breaker, stay out of its
        /// hands (strike and leave, shoot) and never wrestle it; against a swordsman, get past the blade
        /// (behind it, over it); against a Tempest kicker, meet it in the air; against an Iron Fist, break
        /// its guard (grabs, heavy blows) rather than trade flurries; against a Hunter, rush its traps.
        /// The mean over the foe's schools (none: no opinion).
        /// </summary>
        private static float Matchup(Technique t, int level, Actor v)
        {
            if (v == null || !Settings.Schools)
            {
                return 1f;
            }
            Form f = t.FormsAt(level);
            float sum = 0f;
            int n = 0;
            for (int i = 1; i <= Schools.Count; i++)
            {
                if (!Schools.Knows(v, (School)i))
                {
                    continue;
                }
                n++;
                float k = 1f;
                switch ((School)i)
                {
                    case School.KiArts:
                        if ((f & (Form.Dash | Form.Teleport)) != 0) k *= 1.3f;
                        if ((f & (Form.Projectile | Form.Beam)) != 0) k *= 0.85f;
                        break;
                    case School.PhantomStep:
                        if ((f & Form.Area) != 0) k *= 1.3f;
                        if ((f & Form.Fast) != 0) k *= 1.15f;
                        if ((f & Form.Charge) != 0) k *= 0.8f;
                        break;
                    case School.EarthBreaker:
                        if ((f & (Form.Projectile | Form.Beam)) != 0) k *= 1.25f;
                        if ((f & Form.Dash) != 0) k *= 1.1f;
                        if ((f & Form.Grab) != 0) k *= 0.7f;
                        break;
                    case School.ThousandBlades:
                        if ((f & Form.Teleport) != 0) k *= 1.25f;
                        if ((f & (Form.Aerial | Form.Projectile)) != 0) k *= 1.1f;
                        break;
                    case School.TempestLeg:
                        if ((f & (Form.Rising | Form.AntiAir)) != 0) k *= 1.3f;
                        if ((f & Form.Area) != 0) k *= 1.1f;
                        if ((f & Form.Aerial) != 0) k *= 0.9f;
                        break;
                    case School.IronFist:
                        if ((f & (Form.Grab | Form.Heavy)) != 0) k *= 1.2f;
                        if ((f & Form.Projectile) != 0) k *= 1.1f;
                        if ((f & Form.Flurry) != 0) k *= 0.85f;
                        break;
                    case School.HuntersWay:
                        if ((f & (Form.Dash | Form.Teleport)) != 0) k *= 1.2f;
                        if ((f & Form.Area) != 0) k *= 1.1f;
                        break;
                }
                sum += k;
            }
            return n > 0 ? sum / n : 1f;
        }

        private static bool Ranged(Technique t, int level)
        {
            return t != null && (t.FormsAt(level) & (Form.Projectile | Form.Beam)) != 0 && t.Role != Role.Area;
        }

        /// <summary>
        /// Hand to hand when they are close, shots when they are not, and never the same kind of thing all
        /// fight long: a foe within reach is met with the fists, kicks and blades (combos, routes, grabs),
        /// and a fighter whose last two techniques were both shots looks to close in.
        /// </summary>
        private static float Range(Fighter f, Ctx c, Technique t, int level, Plan p)
        {
            bool ranged = Ranged(t, level);
            float k = 1f;
            if (p.Target != null && c.Dist(p.Target) <= 2.6f * Choreo.KOf(c.Self))
            {
                k *= ranged ? 0.55f : 1.35f;
            }
            if (ranged && Ranged(f.LastTech, 2) && Ranged(f.PrevTech, 2))
            {
                k *= 0.6f;
            }
            if (ranged && t.School != School.KiArts && t.School != School.HuntersWay && t.School != School.None)
            {
                k *= 0.8f;   // (the hand-to-hand schools throw things, but it is not what they are for)
            }
            return k;
        }

        /// <summary>
        /// A target already as good as dead: flying to the death its last blow owed it, at death's door,
        /// or burning down to its last scraps. A technique started on it would end on a corpse before its
        /// blows were thrown (its finisher has nothing left to finish).
        /// </summary>
        internal static bool Spent(Actor v)
        {
            if (v == null || !v.isAlive())
            {
                return true;
            }
            if (MeteorSlam.IsDoomed(v) || MeteorSlam.AtDeathsDoor(v))
            {
                return true;
            }
            return v.hasStatus("burning") && v.getHealth() <= Mathf.Max(6f, v.getMaxHealth() * 0.08f);
        }

        /// <summary>
        /// A God facing a worthy foe (another God or a Grandmaster, a titan or worse, its duel opponent)
        /// reaches for the divine techniques: the battles of Gods are where they belong.
        /// </summary>
        private static float Divine(Fighter f, Technique t, Plan p, float score)
        {
            if (t.PrimordialOnly)
            {
                // a Primordial reaches for its own powers first whenever one is ready (their cooldowns pace them)
                return score * 2f + 6000f;
            }
            // (a God's great powers: the divines of no school and its own school's God techniques)
            if (f.Tier < Tier.God || t.Entry < Tier.God || t.Team)
            {
                return score;
            }
            Actor v = p.Target;
            if (v == null || !v.isAlive())
            {
                return score;
            }
            if (Tiers.Of(v) >= Tier.Grandmaster || Weights.Species(v) >= Weight.Titan || Duels.Opponent(f.A) == v)
            {
                // the hand-made divines are spectacles more than damage (a heal, a sweep, a judgment):
                // against a God's health their damage alone scores them far below everything else
                return score * 1.7f + (Forge.Is(t.Id) || t.School != School.None ? 0f : 650f);
            }
            return Tiers.Of(v) >= Tier.Master ? score * 1.25f : score;
        }

        /// <summary>
        /// Repeating yourself is boring: the last technique counts for less, so does one just seen
        /// nearby, and so does replaying the same chain (the same follow-up to the same opener).
        /// </summary>
        private static float Variety(Fighter f, Technique t, Vector2 at)
        {
            // (a small arsenal must not read as one move on repeat: the lower tiers are pushed harder to vary)
            bool low = f.Tier <= Tier.Adept;
            float v = t == f.LastTech ? (low ? 0.4f : 0.55f) : t == f.PrevTech ? (low ? 0.7f : 0.8f) : 1f;
            if (f.Linked && f.LastTech != null && f.ChainSeen(f.LastTech, t))
            {
                v *= 0.6f;
            }
            return v * Director.Freshness(t, at);
        }

        /// <summary>Plays a plan: starts the move, pays for it, starts its cooldown.</summary>
        public static bool Execute(Fighter f, Plan p)
        {
            Actor a = f.A;
            bool ok;
            try
            {
                ok = p.Tech.Begin(a, p);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] begin " + p.Tech.Id + ": " + e);
                ok = false;
            }
            if (!ok)
            {
                return false;
            }
            Tag(a, p);
            // a counter-attack chained off a reactive answer is itself a counter (one layer deeper, readable as such)
            if (p.Linked && f.LinkDepth > 0 && Moves.TryGet(a, out Move nm) && nm.A == a)
            {
                nm.CounterDepth = Mathf.Max(nm.CounterDepth, f.LinkDepth);
            }
            f.LinkDepth = 0;
            Mana.Spend(a, p.Tech, p.Level, p.Linked);
            f.SetCooldown(p.Tech, p.Tech.CooldownFor(a, p.Level));
            if (p.Linked && f.LastTech != null)
            {
                f.RememberChain(f.LastTech, p.Tech);
            }
            f.PrevTech = f.LastTech;
            f.LastTech = p.Tech;
            f.LastStart = Fighter.Now;
            if (p.Linked)
            {
                f.Chain++;
                f.Heat = Mathf.Min(0.1f * (f.Chain - 1), 0.5f);
            }
            else
            {
                f.Chain = 1;
                f.Heat = 0f;
            }
            f.Link = Link.None;
            f.LinkTarget = null;
            Director.Used(p.Tech, a.current_position);
            if (p.AreaRadius > 0f)
            {
                Director.ClaimArea(p.Point, p.AreaRadius, 2.5f, a);
            }
            Started.TryGetValue(p.Tech.Id, out int n);
            Started[p.Tech.Id] = n + 1;
            if (Dev.On)
            {
                Dev.Trace("tech " + p.Tech.Id + " " + Techniques.Roman(p.Level) + " by " + a.asset.id + "#" + a.Sid() + " (" + f.Tier + " " + f.Persona.Main + ") score "
                    + p.Score.ToString("F0") + " x" + p.Shape.ToString("F2") + " count " + p.Count + (p.Linked ? " LINK" : "")
                    + (p.Target != null ? " -> " + p.Target.asset.id + "#" + p.Target.Sid() : ""));
            }
            Hooks.TechniqueStarted?.Invoke(f, p);
            return true;
        }

        /// <summary>Marks the unit's new move with its technique and level (if Begin didn't).</summary>
        private static void Tag(Actor a, Plan p)
        {
            if (Moves.TryGet(a, out Move m) && m.Tech == null)
            {
                m.Tech = p.Tech;
                m.Level = p.Level;
            }
        }

        /// <summary>
        /// Makes a unit perform a technique now (god powers, dev tools): no cooldown, no mana, and
        /// the plan only has to be possible, not worth it.
        /// </summary>
        public static bool Force(Actor a, Technique t, int level, Actor target = null, Vector2? point = null)
        {
            if (a == null || t == null || !Moves.CanPerform(a))
            {
                return false;
            }
            Fighter f = Fighters.Get(a);
            Ctx c = _ctx;
            c.Build(f);
            c.Forced = true;
            c.ForceTarget = target;
            c.ForcePoint = point;
            if (target != null)
            {
                c.Target = target;
                if (!c.Enemies.Contains(target))
                {
                    c.Enemies.Insert(0, target);
                    c.EnemyDist.Insert(0, c.Dist(target));
                }
            }
            Plan p;
            try
            {
                p = t.Plan(c, level);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] forced plan " + t.Id + ": " + e);
                p = null;
            }
            if (p == null)
            {
                return false;
            }
            p.Tech = t;
            p.Level = level;
            bool ok;
            try
            {
                ok = t.Begin(a, p);
            }
            catch (Exception e)
            {
                Debug.LogError("[Sakuga] forced begin " + t.Id + ": " + e);
                ok = false;
            }
            if (ok)
            {
                Tag(a, p);
                f.LastTech = t;
                f.LastStart = Fighter.Now;
                Director.Used(t, a.current_position);
                Started.TryGetValue(t.Id, out int n);
                Started[t.Id] = n + 1;
                Hooks.TechniqueStarted?.Invoke(f, p);
            }
            return ok;
        }

        /// <summary>A move ended: remember when, and open the chain window it leaves.</summary>
        public static void MoveEnded(Move m, bool aborted)
        {
            if (m.Tech == null || !Fighters.TryGet(m.A, out Fighter f))
            {
                return;
            }
            f.LastEnd = Fighter.Now;
            if (m.Tech.Reactive)
            {
                f.Chain = 0;   // an answer starts a fresh chain
            }
            int maxChain = f.Info != null ? f.Info.MaxChain : 2;
            // (no chain onto a body the move's last blow already killed: it only looks alive until the death check)
            if (!aborted && m.EndLink != Link.None && f.Chain < maxChain && Combat.Living(m.A)
                && (m.EndTarget == null || Combat.Living(m.EndTarget)) && CanTake(f, m.EndLink))
            {
                f.Link = m.EndLink;
                f.LinkTarget = m.EndTarget;
                // an answer's opening: what follows it is a counter-attack (readable as one, Brain.Execute)
                f.LinkDepth = m.Tech.Reactive || m.CounterDepth > 0 ? Mathf.Max(1, m.CounterDepth) : 0;
                // the window stays open while the target is still mid-move (a counter waits for it), up to 0.4 s more
                double extra = m.EndTarget != null && Moves.Busy(m.EndTarget) ? 0.4 : 0.0;
                f.LinkUntil = Fighter.Now + 0.4 + extra;
                f.ThinkIn = 0f;
                f.LinkThinkIn = 0f;
            }
            else
            {
                if (!Moves.Busy(m.A))
                {
                    f.Heat = 0f;   // the chain's extra damage ends with it (a later counter or forced move is no link)
                }
                f.ThinkIn = Mathf.Min(f.ThinkIn, 0.05f);   // no chain: look again right away (no dead air)
            }
        }

        /// <summary>Does the fighter know a technique that takes this link and is ready and affordable?</summary>
        public static bool CanTake(Fighter f, Link link)
        {
            foreach (Technique t in Techniques.All)
            {
                // (the same techniques Choose may pick: a window no plan can use would only hold the fighter)
                if (t.GodAbility || t.Team && !Settings.TeamTechniques || !t.Accepts(link))
                {
                    continue;
                }
                int level = Techniques.Level(f.A, t, f.Tier);
                if (level > 0 && f.Ready(t) && Mana.Has(f.A, t, level, true))
                {
                    return true;
                }
            }
            return false;
        }

        public static string Stats()
        {
            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in Started)
            {
                parts.Add(kv.Key + " " + kv.Value);
            }
            return string.Join(", ", parts);
        }
    }

    /// <summary>
    /// The battle director: keeps battles varied and keeps units from being in two set pieces at
    /// once (a duel, a team technique...). It remembers which techniques were just shown where, so
    /// a battlefield shows a mix instead of the same move ten times.
    /// </summary>
    internal static class Director
    {
        private struct Use
        {
            public Technique T;
            public Vector2 At;
            public double When;
        }

        private static readonly List<Use> _recent = new List<Use>();
        private static readonly Dictionary<long, KeyValuePair<object, double>> _scenes = new Dictionary<long, KeyValuePair<object, double>>();

        public static void Used(Technique t, Vector2 at)
        {
            _recent.Add(new Use { T = t, At = at, When = Fighter.Now });
            if (_recent.Count > 48)
            {
                _recent.RemoveAt(0);
            }
        }

        /// <summary>1 for something not seen lately around here, down to 0.5 for the move everyone nearby just did.</summary>
        public static float Freshness(Technique t, Vector2 at)
        {
            double now = Fighter.Now;
            float k = 1f;
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                Use u = _recent[i];
                if (now - u.When > 12.0)
                {
                    break;
                }
                if (u.T == t && (u.At - at).sqrMagnitude < 15f * 15f)
                {
                    k *= 0.85f;
                }
            }
            return Mathf.Max(0.5f, k);
        }

        /// <summary>Has this technique been used anywhere within 'seconds'?</summary>
        public static bool SeenRecently(Technique t, float seconds)
        {
            double now = Fighter.Now;
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (now - _recent[i].When > seconds)
                {
                    return false;
                }
                if (_recent[i].T == t)
                {
                    return true;
                }
            }
            return false;
        }

        // ----------------------------------------------------------- areas --

        private struct Area
        {
            public Vector2 At;
            public float R;
            public double Until;
            public Actor By;
        }

        private static readonly List<Area> _areas = new List<Area>();

        /// <summary>Someone is about to strike this area: others should pick another crowd.</summary>
        public static void ClaimArea(Vector2 at, float radius, float seconds, Actor by)
        {
            _areas.Add(new Area { At = at, R = radius, Until = Fighter.Now + seconds, By = by });
            if (_areas.Count > 32)
            {
                _areas.RemoveAt(0);
            }
        }

        /// <summary>1 if no ally is already striking around here, down to 0.15 if one is right on it.</summary>
        public static float AreaFree(Vector2 at, float radius, Actor self)
        {
            double now = Fighter.Now;
            float k = 1f;
            for (int i = _areas.Count - 1; i >= 0; i--)
            {
                Area a = _areas[i];
                if (a.Until < now)
                {
                    _areas.RemoveAt(i);
                    continue;
                }
                if (a.By == self || a.By == null || self == null || a.By.kingdom != self.kingdom)
                {
                    continue;
                }
                float d = Vector2.Distance(a.At, at);
                float overlap = 1f - Mathf.Clamp01(d / (a.R + radius));
                k *= 1f - 0.85f * overlap;
            }
            return Mathf.Max(0.15f, k);
        }

        // ---------------------------------------------------------- scenes --

        /// <summary>Claims units for a set piece for up to 'seconds'. Fails (claims nothing) if any is already in one.</summary>
        public static bool Claim(object scene, float seconds, params Actor[] actors)
        {
            double now = Fighter.Now;
            foreach (Actor a in actors)
            {
                if (a == null)
                {
                    continue;
                }
                if (_scenes.TryGetValue(a.Sid(), out KeyValuePair<object, double> s) && s.Value > now && s.Key != scene)
                {
                    return false;
                }
            }
            foreach (Actor a in actors)
            {
                if (a != null)
                {
                    _scenes[a.Sid()] = new KeyValuePair<object, double>(scene, now + seconds);
                }
            }
            return true;
        }

        public static void Release(object scene)
        {
            List<long> drop = null;
            foreach (KeyValuePair<long, KeyValuePair<object, double>> kv in _scenes)
            {
                if (kv.Value.Key == scene)
                {
                    (drop ??= new List<long>()).Add(kv.Key);
                }
            }
            if (drop != null)
            {
                foreach (long id in drop)
                {
                    _scenes.Remove(id);
                }
            }
        }

        public static void Release(Actor a)
        {
            if (a != null)
            {
                _scenes.Remove(a.Sid());
            }
        }

        public static bool InScene(Actor a)
        {
            return a != null && _scenes.TryGetValue(a.Sid(), out KeyValuePair<object, double> s) && s.Value > Fighter.Now;
        }

        public static object SceneOf(Actor a)
        {
            return a != null && _scenes.TryGetValue(a.Sid(), out KeyValuePair<object, double> s) && s.Value > Fighter.Now ? s.Key : null;
        }

        public static void Clear()
        {
            _recent.Clear();
            _scenes.Clear();
            _areas.Clear();
        }
    }
}
