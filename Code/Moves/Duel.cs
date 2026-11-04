using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// One duel: the controller that drives a pair of fighters from the first look to the last
    /// blow. One per pair; it validates both every tick and lets everything go the moment either
    /// dies, is picked up, possessed, or wanders off.
    ///
    ///   OPENING   Gods: CLASH OF HEAVENS. Experts and up (and rivals): STARE-DOWN, then FIRST
    ///             CLASH. Adepts: FIRST CLASH.
    ///   BOUTS     a bout holds both fighters (Director.Claim, two Duelist moves) while one or
    ///             more exchanges play: a pool picked by weights from the circumstances (tiers,
    ///             health, weapons, size, ground, history) and never the same twice in a row (two
    ///             Gods also have a second trip to the sky, once). Grandmasters and Gods add the
    ///             CATASTROPHES (Skyfall, Tectonic Clash, Sky Battle, Mountain Breaker, Relentless
    ///             Chase): the fight leaves where it stood and wrecks the world on its way, carried
    ///             on across the map in one direction (Heading) rather than back and forth. A Circle
    ///             is a breath and chains straight into the next fight; a fight may flow straight
    ///             into another (more often the higher the tiers: at most three to a bout, four for
    ///             Grandmasters, five for Gods); three even clashes in a row chain into a CLASH SPIRAL.
    ///   FREE      between bouts both are released, aimed at each other, so the Brain's own
    ///             techniques fly: Adepts for 1.4-2.2 s, Experts about a second, Masters and up
    ///             (the Flow tiers) only 0.3-0.8 s, so a duel of masters is one continuous flow of
    ///             exchanges, techniques and strikes. The winner of the last exchange is handed a
    ///             chain link onto the loser (Close or Down), so its technique follows the exchange
    ///             at once. The duel rations the techniques so they punctuate the duel instead of
    ///             swallowing it (an exchange of techniques, one pressing with a chain, one alone,
    ///             rarely none): once the free fighting is over the second answers the first (the
    ///             moment it is on its feet, if the Brain finds a technique worth it), nothing more
    ///             is handed out while a technique plays, and the next bout starts the moment both
    ///             are free. Only when the bout can't begin with nobody mid-technique (apart, or one
    ///             reeling for a while) is it free fighting again.
    ///             What they deal each other there is softened, one technique costs at most its
    ///             share of the other's health, and nothing is lethal (Duels.Pace). A duelist never
    ///             flees (the game's own "run away" is called off): only the Final ends a duel.
    ///   COURSE    every duel has a course (Adepts 13-18 s, Experts 18-25, Masters 24-33,
    ///             Grandmasters 36-50, two Gods 72-98): the opponent's blows can't take a duelist
    ///             under its floor, a share of the health it started with that falls as the course
    ///             runs (Floor). However hard the blows (and the tiers' Might makes them hard), a
    ///             duel between equals is never over in seconds.
    ///   THE END   when one drops below a quarter of its health (and has no Second Wind left), or
    ///             when the duel has run its course, the FINAL EXCHANGE: dash through each other,
    ///             back to back, the loser falls (sometimes both drop to one knee and it goes on).
    ///             The winner: Progression.DuelWon. Wandering apart (12 tiles for 4 s; Grandmasters and Gods 22
    ///             for 6 s), peace, or well past its course and the duel dissolves; two who both
    ///             walk away from a duel become Rivals (Duels.Parted).
    ///
    /// The world takes the blows (Wreck: Kit.Ravage): level II impacts scorch and crater it, level
    /// III blast it (the big ones set it alight), a God's one force harder and half as wide again.
    ///
    /// The clock is move time (paused with the game, at most x3 at high game speeds).
    /// </summary>
    internal sealed class Duel
    {
        /// <summary>Move seconds after which a duel that never reached its end dissolves (well past its course).</summary>
        public float MaxLength => Mathf.Max(40f, _finalAt * 1.4f + 10f);
        /// <summary>Of the loser's health, what one exchange's chip (mult 1) costs it (a little less in the long duels).</summary>
        private float ChipFrac => 0.12f * Mathf.Clamp(Mathf.Sqrt(30f / Mathf.Max(10f, _finalAt)), 0.6f, 1.1f);

        public readonly Actor A, B;
        public readonly long IdA, IdB;
        public readonly bool Rivals;

        public Tier TA, TB;
        /// <summary>Each fighter's visual level (I Adept, II Expert, III Master and up) and the duel's (the higher).</summary>
        public int LvA, LvB, L;
        public Palette PA, PB;
        /// <summary>Size vs a human (each, and the pair's average) and the finer pixel size of the two.</summary>
        public float KA, KB, K, Px;

        /// <summary>Move time since it began.</summary>
        public float T;
        public bool Over;
        /// <summary>At least one exchange was played (they really dueled).</summary>
        public bool Fought;
        public bool Awarded;
        public bool KneelUsed;
        /// <summary>Two Gods went back up to the sky mid-duel (once).</summary>
        public bool SkyUsed;
        public bool SpiralPending;
        public int Exchanges, Clashes, EvenStreak, Spirals;
        public string Last, Prev, Prev2;
        public Vector2 Mid;
        public bool Visible;
        /// <summary>Catastrophes played, the last one, and which way they have been carrying the fight (zero: nowhere yet).</summary>
        public int Cats;
        public string LastCat;
        public Vector2 Heading;

        private enum Stage
        {
            Free,
            Bout
        }

        private Stage _stage = Stage.Free;
        private Duelist _ma, _mb;
        private Exchange _ex;
        private readonly List<Exchange> _queue = new List<Exchange>();
        private float _free, _far, _aim;
        /// <summary>Time left in the free fighting proper, and how long the next bout has been waiting since.</summary>
        private float _window, _waited;
        private float _momentum;
        private bool _goneA, _goneB, _ending, _cine;
        private long _killer = -1, _victim = -1;
        /// <summary>When the duel has run its course and the Final comes whatever their health.</summary>
        private readonly float _finalAt;
        /// <summary>Exchanges played in the current bout; Finals that ended with both standing.</summary>
        private int _bout, _finals;
        /// <summary>Techniques each may start in this free fighting, and how many it has started.</summary>
        private int _allowA, _allowB, _usedA, _usedB;
        /// <summary>
        /// The answer: once one has shown its technique, the other's turn of this free fighting (its
        /// own, unused) comes the moment it is on its feet, for this long. Given once each.
        /// </summary>
        private float _ansA, _ansB;
        private bool _answeredA, _answeredB;
        /// <summary>An answer has closed: the bout comes as soon as nobody is mid-technique.</summary>
        private bool _answerOver;
        /// <summary>Who had the upper hand at the end of the last bout (it leads the next free fighting; 0: nobody).</summary>
        private long _lead;
        /// <summary>Times this free fighting was opened again because the bout could not begin (each wait longer).</summary>
        private int _refills;
        /// <summary>The bout's winner just handed its follow-up link (its technique is its share of the free fighting; 0: none).</summary>
        private long _followUp;
        /// <summary>Each one's Brain has had a look since the free fighting (or its answer) opened; the ThinkIn last seen.</summary>
        private bool _lookedA, _lookedB;
        private float _thinkA, _thinkB;
        /// <summary>The first free fighting after the opening (both test the other once).</summary>
        private bool _tested;
        /// <summary>The technique of each that is landing on the other now, and the health it has taken so far.</summary>
        private Move _capA, _capB;
        private float _dealtA, _dealtB;
        /// <summary>Why it ended (the dev log).</summary>
        private string _why = "";
        private float _watch;
        /// <summary>Each one's health (share) when the duel began: its floor falls from there over the course.</summary>
        private readonly float _hp0A, _hp0B;
        /// <summary>Just let go: check its footing (a fresh crater may be burning under it) for this long.</summary>
        private bool _footA, _footB;
        private float _footFor;

        private static readonly List<Exchange> _pick = new List<Exchange>();
        private static readonly List<float> _weights = new List<float>();

        /// <summary>World time it began (a God ability marked since then was spent in this duel).</summary>
        public readonly double Began = Fighter.Now;

        /// <summary>The techniques each duelist has thrown in this duel and how often (Duels.Freshness).</summary>
        private readonly Dictionary<long, Dictionary<Technique, int>> _usedTech = new Dictionary<long, Dictionary<Technique, int>>();

        public void NoteUsed(Actor a, Technique t)
        {
            if (a == null || t == null)
            {
                return;
            }
            if (!_usedTech.TryGetValue(a.Sid(), out Dictionary<Technique, int> m))
            {
                _usedTech[a.Sid()] = m = new Dictionary<Technique, int>();
            }
            m.TryGetValue(t, out int n);
            m[t] = n + 1;
        }

        public int UsedCount(Actor a, Technique t)
        {
            return a != null && t != null && _usedTech.TryGetValue(a.Sid(), out Dictionary<Technique, int> m) && m.TryGetValue(t, out int n) ? n : 0;
        }

        /// <summary>The techniques 'id' threw in this duel (Duels remembers them for its next duel).</summary>
        public IEnumerable<Technique> UsedBy(long id)
        {
            return _usedTech.TryGetValue(id, out Dictionary<Technique, int> m) ? (IEnumerable<Technique>)m.Keys : new Technique[0];
        }

        public Duel(Actor a, Actor b, bool rivals)
        {
            A = a;
            B = b;
            IdA = a.Sid();
            IdB = b.Sid();
            Rivals = rivals;
            Refresh();
            // the course: how long these two take to wear each other down (the floor follows it)
            Tier low = Low;
            float course = low >= Tier.God ? Random.Range(72f, 98f)
                : low == Tier.Grandmaster ? Random.Range(36f, 50f)
                : low == Tier.Master ? Random.Range(24f, 33f)
                : low == Tier.Expert ? Random.Range(18f, 25f)
                : low == Tier.Adept ? Random.Range(13f, 18f)
                : Random.Range(10f, 14f);
            _finalAt = course + (rivals ? (low >= Tier.God ? 8f : 4f) : 0f);
            _hp0A = Mathf.Clamp(Combat.HpPct(a), 0.3f, 1f);
            _hp0B = Mathf.Clamp(Combat.HpPct(b), 0.3f, 1f);
        }

        // ------------------------------------------------------------ queries --

        /// <summary>The exchange playing now (null between bouts).</summary>
        public Exchange Current => _ex;

        /// <summary>The exchanges played so far, in order (ids; the later duel rework and the variety test read it).</summary>
        public readonly List<string> History = new List<string>();

        public bool Has(long id)
        {
            return id == IdA || id == IdB;
        }

        public Actor OpponentOf(Actor a)
        {
            long id = a.Sid();
            return id == IdA ? B : id == IdB ? A : null;
        }

        /// <summary>Where each stands right now (its pose while held, else the unit).</summary>
        public Vector2 PosA => _ma != null && !_ma.Finished ? _ma.Pose.Ground : A.current_position;

        public Vector2 PosB => _mb != null && !_mb.Finished ? _mb.Pose.Ground : B.current_position;

        public float HpA => Combat.HpPct(A);

        public float HpB => Combat.HpPct(B);

        public float PowerA => Tiers.Power(A);

        public float PowerB => Tiers.Power(B);

        /// <summary>The stronger's power rating over the weaker's (1 = equals).</summary>
        public float Ratio => Mathf.Max(PowerA, PowerB) / Mathf.Max(0.01f, Mathf.Min(PowerA, PowerB));

        public bool ArmedA => A.hasWeapon();

        public bool ArmedB => B.hasWeapon();

        public static int Lv(Tier t)
        {
            return t >= Tier.Master ? 3 : t == Tier.Expert ? 2 : 1;
        }

        /// <summary>
        /// What a technique of one duelist does to the other outside the exchanges (techniques are
        /// tuned to sweep armies, and the tiers' Might makes them hit harder still: between two
        /// equals at full strength one would end it in a blink).
        /// </summary>
        public float TechScale => Top >= Tier.God ? 0.16f : Top == Tier.Grandmaster ? 0.2f : Top == Tier.Master ? 0.24f : Top == Tier.Expert ? 0.3f : 0.42f;

        /// <summary>
        /// The most one technique of one duelist may cost the other outside the exchanges, as a share
        /// of the victim's health: a Hundred Fists III between Masters would otherwise take a third
        /// of it, and the techniques, not the duel, would decide it.
        /// </summary>
        public float TechCap => Low >= Tier.God ? 0.07f : Low == Tier.Grandmaster ? 0.08f : Low == Tier.Master ? 0.09f : Low == Tier.Expert ? 0.11f : 0.14f;

        /// <summary>What a plain swing or arrow of one duelist does to the other (the stronger the pair, the less a plain blow decides).</summary>
        public float SwingScale => Gods ? 0.35f : L >= 3 ? 0.45f : L == 2 ? 0.5f : 0.6f;

        /// <summary>Seconds a technique once shown stays "read" by the other (the masters run through more of their repertoire).</summary>
        public float ReadFor => Low >= Tier.God ? 7f : Low == Tier.Grandmaster ? 9f : Low == Tier.Master ? 10f : Low == Tier.Expert ? 12f : 14f;

        /// <summary>Two Combat Gods: the duel of the age.</summary>
        public bool Gods => TA == Tier.God && TB == Tier.God;

        /// <summary>The weaker and the stronger tier of the pair.</summary>
        public Tier Low => TA < TB ? TA : TB;

        public Tier Top => TA > TB ? TA : TB;

        /// <summary>Both Masters or better: they fight in one continuous flow (TierInfo.Flow).</summary>
        public bool Flow => Low >= Tier.Master;

        /// <summary>Both Grandmasters or better: the catastrophes are theirs (two Gods: the full scale).</summary>
        public bool Grand => Low >= Tier.Grandmaster;

        /// <summary>
        /// How long they rest inside an exchange (settling into guard, finding their feet after a
        /// skid, a breather): Masters and up land crisply and go.
        /// </summary>
        public float Brisk => Low >= Tier.God ? 0.4f : Low == Tier.Grandmaster ? 0.45f : Low == Tier.Master ? 0.55f : Low == Tier.Expert ? 0.8f : 1f;

        /// <summary>Exchanges a bout may hold before both are let go.</summary>
        private int MaxBout => Gods ? 5 : Grand ? 4 : 3;

        /// <summary>The free fighting is over: the next exchange starts the moment both are free (Duels.Waiting).</summary>
        public bool Waiting => !Over && _stage == Stage.Free && Fought && _window <= 0f;

        // -------------------------------------------------------------- course --

        /// <summary>
        /// The least health (a share of its max) the opponent's blows may leave 'v' with right now:
        /// from the health it began with, falling over the duel's course to almost nothing at its
        /// end ('deep': a God's own ability, the turning point of a duel, cuts deeper).
        /// </summary>
        public float Floor(Actor v, bool deep = false)
        {
            float h0 = v != null && v.Sid() == IdB ? _hp0B : _hp0A;
            float u = Mathf.Clamp01(T / Mathf.Max(5f, _finalAt));
            float f = h0 * (1f - 0.9f * Mathf.Pow(u, 0.85f)) - (deep ? 0.15f : 0f);
            return Mathf.Max(0.04f, f);
        }

        /// <summary>Floor in health points.</summary>
        public float FloorHp(Actor v, bool deep = false)
        {
            return Floor(v, deep) * Mathf.Max(1, v.getMaxHealth());
        }

        /// <summary>What Combat.Hit multiplies an attacker's blows by (its boosts, its tier's Might, the Power setting).</summary>
        private static float Boost(Actor a)
        {
            TierInfo ti = Tiers.Info(Tiers.Of(a));
            return Mathf.Max(0.05f, Fighters.DamageMult(a) * (ti != null ? ti.Might : 1f) * Settings.Power);
        }

        // ------------------------------------------------------------- world --

        /// <summary>
        /// The world takes the blow (Kit.Ravage): 'force' and 'radius' by the level's grammar (II
        /// scorches and craters, III blasts and burns); a God's one force harder (at most 4) and half
        /// as wide again. 'by' is spared its own kingdom's buildings (null for an even clash).
        /// 'fire' false: blasted but not set alight (the many ordinary clashes of a long duel would
        /// otherwise bury the fight in flames; the catastrophes and the heavy impacts burn). Never
        /// alight while one of them can burn (an Expert facing a Master): its own duel's flames
        /// would burn it down outside the Final.
        /// </summary>
        public void Wreck(Vector2 at, float radiusTiles, int force, Actor by, bool fire = true)
        {
            if (force <= 0)
            {
                return;
            }
            Tier t = by != null ? Tiers.Of(by) : Top;
            if (t >= Tier.God)
            {
                force++;
                radiusTiles *= 2f;
                // two Gods fighting split the land: every big blow throws a fissure out across it
                if (force >= 3 && radiusTiles >= 1.2f && Random.value < 0.6f)
                {
                    Vector2 dir = Random.insideUnitCircle.normalized;
                    Vector2 to = Combat.Clamp(at + dir * radiusTiles * Random.Range(2f, 3.5f));
                    Kit.Trench(at, to, 0.8f, Mathf.Min(4, force), by, false, null, 0f, by == null ? A : null, by == null ? B : null);
                    if (Visible)
                    {
                        DuelFx.Fissure(at, to, Px, Combat.TileAt(at), by != null ? Palette.For(t, 3).Edge : Color.white, 2);
                    }
                }
            }
            else if (t == Tier.Grandmaster)
            {
                radiusTiles *= 1.35f;
            }
            if (fire && (Burns(A, IdA) || Burns(B, IdB)))
            {
                fire = false;
            }
            // (an even clash credits nobody and spares both duelists' kingdoms)
            Kit.Ravage(at, radiusTiles, Mathf.Min(4, force), by, fire, null, 0f, by == null ? A : null, by == null ? B : null);
        }

        /// <summary>Still this duelist, alive, and fire hurts it (below Master: no fire immunity).</summary>
        private static bool Burns(Actor a, long id)
        {
            return a != null && a.Sid() == id && a.isAlive() && !Combat.Fireproof(a);
        }

        /// <summary>The same along a line (a body ploughed through the ground, a cut that runs on into the earth).</summary>
        public void Furrow(Vector2 a, Vector2 b, float widthTiles, int force, Actor by)
        {
            if (force <= 0)
            {
                return;
            }
            Tier t = by != null ? Tiers.Of(by) : Top;
            if (t >= Tier.God)
            {
                force++;
                widthTiles *= 1.25f;
            }
            Kit.Trench(a, b, widthTiles, Mathf.Min(4, force), by);
        }

        // ------------------------------------------------------- techniques --

        /// <summary>
        /// Hooks.HoldTechniques for a duelist: the Brain may start a technique only in the free
        /// fighting, only as many as this free fighting allows it, the first only while the free
        /// fighting has time left (for the Flow tiers all of it: their free fighting is a breath
        /// long) or as its answer to the other's technique, a second only as a chain off the first.
        /// Once its share is spent, nothing more until the next bout has been fought: a fighter
        /// never strings technique after technique onto the other while the duel waits.
        /// </summary>
        public bool HoldTech(Actor a)
        {
            if (Over)
            {
                return false;
            }
            if (_stage != Stage.Free || !Fought || FinalDue())
            {
                return true;
            }
            bool isA = a.Sid() == IdA;
            // a wind-up in the free fighting is an opening: the other may always answer it (the
            // Brain's tactics reach for an interrupt, a dash or a blink first)
            Actor other = isA ? B : A;
            if (Moves.Charging(other))
            {
                return false;
            }
            int used = isA ? _usedA : _usedB, allow = isA ? _allowA : _allowB;
            if (used >= allow)
            {
                return true;
            }
            if (used == 0)
            {
                return _window < (Flow ? 0.02f : Low == Tier.Expert ? 0.2f : 0.35f) && (isA ? _ansA : _ansB) <= 0f;
            }
            return !(Fighters.TryGet(a, out Fighter f) && f.Linked);
        }

        /// <summary>In a technique of its own (not the duel's hold, not someone's puppet, not a light flow strike or blur).</summary>
        private static bool Pressing(Actor a)
        {
            return Moves.TryGet(a, out Move m) && m.Tech != null && !m.Interruptible && !(m is Duelist) && !(m is Puppet);
        }

        /// <summary>
        /// The longest an answer stays open: the Brain looks at once (it is told to), so this only
        /// covers a look that comes a frame or two late; the moment it has looked and chosen
        /// nothing, the answer closes (Answers) and the next bout comes.
        /// </summary>
        private float AnswerTime => Low >= Tier.God ? 0.3f : Low == Tier.Grandmaster ? 0.35f : Low == Tier.Master ? 0.4f : Low == Tier.Expert ? 0.6f : 0.8f;

        /// <summary>
        /// The other has shown its technique in this free fighting (and come out of it) and 'x' (its
        /// own share unused) is on its feet, its own last technique far enough behind it for the
        /// Brain to take another: its answer, now (once). The next bout waits for it.
        /// </summary>
        private void Answer(Actor x, bool isA)
        {
            if (isA ? _answeredA || _usedA > 0 || _allowA <= 0 || _usedB == 0 : _answeredB || _usedB > 0 || _allowB <= 0 || _usedA == 0)
            {
                return;
            }
            if (!Ready(x) || Pressing(isA ? B : A) || !Fighters.TryGet(x, out Fighter f))
            {
                return;   // reeling, or the other is still in its technique (nothing could touch it yet)
            }
            float gap = (f.Info != null ? f.Info.MinGap : 2.5f) / Mathf.Max(0.1f, Settings.Frequency);
            if (Fighter.Now - f.LastEnd < gap)
            {
                return;   // the Brain would not take a technique yet (a parry's riposte just ended): a moment
            }
            float t = AnswerTime;
            if (isA)
            {
                _answeredA = true;
                _ansA = t;
            }
            else
            {
                _answeredB = true;
                _ansB = t;
            }
            _free = Mathf.Max(_free, t);
            f.ThinkIn = 0f;
            if (isA)
            {
                _thinkA = 0f;
                _lookedA = false;
            }
            else
            {
                _thinkB = 0f;
                _lookedB = false;
            }
            Duels.Trace("duel " + IdA + "/" + IdB + " " + x.asset.id + "#" + x.Sid() + " answers");
        }

        /// <summary>
        /// Open answers run down; one closes as soon as its technique has started, or as soon as the
        /// Brain has looked and found nothing worth it (no dead air waiting on a technique that won't come).
        /// </summary>
        private void Answers(float dt)
        {
            if (_ansA > 0f)
            {
                _ansA = _usedA > 0 || _lookedA ? 0f : Mathf.Max(0f, _ansA - dt);
                _answerOver |= _ansA <= 0f;
            }
            if (_ansB > 0f)
            {
                _ansB = _usedB > 0 || _lookedB ? 0f : Mathf.Max(0f, _ansB - dt);
                _answerOver |= _ansB <= 0f;
            }
            if (_answerOver && _ansA <= 0f && _ansB <= 0f && !Pressing(A) && !Pressing(B))
            {
                _answerOver = false;
                _free = Mathf.Min(_free, Flow ? 0.05f : 0.15f);   // the answer is over (or never came): the bout, now
            }
        }

        /// <summary>
        /// Notes when each one's Brain has had a look (a look sets its next one ahead: ThinkIn rises;
        /// everything else only ever lowers it).
        /// </summary>
        private void Looks()
        {
            if (Fighters.TryGet(A, out Fighter fa))
            {
                if (fa.ThinkIn > _thinkA + 0.005f)
                {
                    _lookedA = true;
                }
                _thinkA = fa.ThinkIn;
            }
            if (Fighters.TryGet(B, out Fighter fb))
            {
                if (fb.ThinkIn > _thinkB + 0.005f)
                {
                    _lookedB = true;
                }
                _thinkB = fb.ThinkIn;
            }
        }

        /// <summary>Starts watching for the Brains' next looks (a new free fighting).</summary>
        private void WatchLooks()
        {
            _lookedA = _lookedB = false;
            _thinkA = Fighters.TryGet(A, out Fighter fa) ? fa.ThinkIn : 0f;
            _thinkB = Fighters.TryGet(B, out Fighter fb) ? fb.ThinkIn : 0f;
        }

        /// <summary>A duelist started a technique (Hooks.TechniqueStarted).</summary>
        public void NoteTechnique(Actor a)
        {
            if (Over || _stage != Stage.Free)
            {
                return;
            }
            if (a.Sid() == IdA)
            {
                _usedA++;
            }
            else
            {
                _usedB++;
            }
        }

        /// <summary>Health that technique 'm' of 'x' may still take from the other (TechCap of its health, less what it took).</summary>
        public float TechRoom(Actor x, Move m, Actor v)
        {
            // a God's own ability (Zero Moment, Starfall) is the turning point of a duel: it cuts deeper
            float share = m != null && m.Tech != null && m.Tech.GodAbility ? Mathf.Max(0.25f, TechCap) : TechCap;
            float cap = share * Mathf.Max(1, v.getMaxHealth());
            if (x.Sid() == IdA)
            {
                if (_capA != m)
                {
                    _capA = m;
                    _dealtA = 0f;
                }
                return Mathf.Max(0f, cap - _dealtA);
            }
            if (_capB != m)
            {
                _capB = m;
                _dealtB = 0f;
            }
            return Mathf.Max(0f, cap - _dealtB);
        }

        public void TechDealt(Actor x, float hp)
        {
            if (x.Sid() == IdA)
            {
                _dealtA += hp;
            }
            else
            {
                _dealtB += hp;
            }
        }

        /// <summary>
        /// What this free fighting holds: an exchange of techniques (one each: the second answers the
        /// first), one pressing with a chain, one alone (the one with the upper hand), or none at all
        /// (straight back to the blades). The first time after the opening both test the other once.
        /// Each is a few seconds at most: the bouts, not the techniques, carry a duel.
        /// </summary>
        private void Ration()
        {
            _usedA = _usedB = 0;
            _ansA = _ansB = 0f;
            _answeredA = _answeredB = false;
            _answerOver = false;
            WatchLooks();
            if (!Fought)
            {
                _allowA = _allowB = 0;
                return;
            }
            if (!_tested)
            {
                _tested = true;
                _allowA = _allowB = 1;
                return;
            }
            // the masters fight with their techniques: rarely a free fighting without them, chains more
            // often the higher the tiers
            Tier low = Low;
            // (techniques and their chains are what a duel between schools is made of: windows without one are rare)
            float none = low >= Tier.God ? 0.03f : low == Tier.Grandmaster ? 0.05f : low == Tier.Master ? 0.06f : low == Tier.Expert ? 0.07f : 0.08f;
            float press = low >= Tier.God ? 0.42f : low == Tier.Grandmaster ? 0.38f : low == Tier.Master ? 0.34f : low == Tier.Expert ? 0.3f : 0.26f;
            float one = 0.3f;
            // whoever had the upper hand leads (the last bout's winner, then momentum, then health, then chance)
            bool aLeads = _lead == IdA || _lead != IdB
                && (Mathf.Abs(_momentum) > 0.25f ? _momentum > 0f : Mathf.Abs(HpA - HpB) > 0.1f ? HpA > HpB : Random.value < 0.5f);
            float r = Random.value;
            if (r < none)
            {
                _allowA = _allowB = 0;
            }
            else if (r < none + press)
            {
                _allowA = aLeads ? (low >= Tier.Master ? 3 : 2) : 0;   // a combo: a technique and a chain off it
                _allowB = aLeads ? 0 : (low >= Tier.Master ? 3 : 2);
            }
            else if (r < none + press + one)
            {
                bool a = Random.value < 0.7f ? aLeads : !aLeads;   // now and then the one on the back foot strikes back
                _allowA = a ? 1 : 0;
                _allowB = a ? 0 : 1;
            }
            else
            {
                _allowA = _allowB = 1;   // a technique each: the second answers the first
            }
        }

        /// <summary>Tiers, colours and sizes as they are now (a promotion mid-duel shows at the next bout).</summary>
        public void Refresh()
        {
            TA = Tiers.Of(A);
            TB = Tiers.Of(B);
            LvA = Lv(TA);
            LvB = Lv(TB);
            L = Mathf.Max(LvA, LvB);
            PA = Palette.For(TA, LvA);
            PB = Palette.For(TB, LvB);
            KA = Mathf.Sqrt(Mathf.Clamp(A.actor_scale / 0.1f, 0.6f, 4f));
            KB = Mathf.Sqrt(Mathf.Clamp(B.actor_scale / 0.1f, 0.6f, 4f));
            K = (KA + KB) * 0.5f;
            Px = Mathf.Min(Mathf.Clamp(A.actor_scale, 0.04f, 0.4f), Mathf.Clamp(B.actor_scale, 0.04f, 0.4f));
        }

        // ------------------------------------------------------------- begin --

        /// <summary>Claims both and lines up the opening. False: one of them is taken.</summary>
        public bool Begin()
        {
            if (!Director.Claim(this, 3f, A, B))
            {
                return false;
            }
            Opening();
            Aim(A, B);
            Aim(B, A);
            Free(0f);
            UpdateMid();
            return true;
        }

        /// <summary>
        /// How it opens. Never the same ceremony every time (that is what made God duels rehearsed):
        /// two Gods most often take it to the sky (the Clash of Heavens), but they may also stare each
        /// other down and clash, clash at once, or simply start fighting (the first bout picked by
        /// their personas from the pool); Experts and up stare first, unless both are restless
        /// (pressing personas rush in); rivals always stare. Duels.Openings may set it instead.
        /// </summary>
        private void Opening()
        {
            for (int i = 0; i < Duels.Openings.Count; i++)
            {
                try
                {
                    if (Duels.Openings[i](this, _queue))
                    {
                        return;
                    }
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("duel opening", e);
                }
                _queue.Clear();
            }
            bool gods = TA == Tier.God && TB == Tier.God;
            try
            {
                if (Openers.Fill(this, _queue, gods, TA >= Tier.Expert && TB >= Tier.Expert, Rivals))
                {
                    return;
                }
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("duel opener", e);
            }
            _queue.Clear();
            Persona pa = Personas.Of(A), pb = Personas.Of(B);
            bool restless = pa.Aggression + pb.Aggression > 1.45f;
            float r = Random.value;
            if (gods && !Rivals)
            {
                if (r < 0.45f)
                {
                    _queue.Add(new Heavens());
                }
                else if (r < 0.68f && !restless)
                {
                    _queue.Add(new StareDown());
                    _queue.Add(new FirstClash());
                }
                else if (r < 0.85f)
                {
                    _queue.Add(new FirstClash());
                }
                // else: no ceremony at all, the first bout comes from the pool
                return;
            }
            if (gods)
            {
                _queue.Add(new StareDown());   // a rematch of Gods stares first, then the sky
                _queue.Add(new Heavens());
                return;
            }
            if ((TA >= Tier.Expert && TB >= Tier.Expert) || Rivals)
            {
                if (Rivals || !restless || r < 0.5f)
                {
                    _queue.Add(new StareDown());
                }
                if (r < 0.85f || _queue.Count == 0)
                {
                    _queue.Add(new FirstClash());
                }
                return;
            }
            _queue.Add(new FirstClash());
        }

        // -------------------------------------------------------------- tick --

        public void Tick(float dt)
        {
            if (Over)
            {
                return;
            }
            T += dt;
            if (!_goneA && !Valid(A, IdA))
            {
                Lose(true);
            }
            if (!Over && !_goneB && !Valid(B, IdB))
            {
                Lose(false);
            }
            if (Over)
            {
                return;
            }
            UpdateMid();
            if (Visible && Low >= Tier.Grandmaster)
            {
                try
                {
                    DuelAmbience.Tick(this, dt);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("duel ambience", e);
                }
            }
            if (Dev.On && _stage == Stage.Bout && Duels.Watched == this && (Visible || _ex is Catastrophe))
            {
                // dev captures keep the duel's exchanges in frame as it moves about (the techniques of
                // the free fighting frame themselves); a catastrophe is followed across the map
                _watch -= dt;
                if (_watch <= 0f)
                {
                    _watch = 0.2f;
                    Dev.Watch(Mid + new Vector2(0f, 1.5f * K + Lift));
                }
            }
            if (_stage == Stage.Bout)
            {
                BoutTick(dt);
            }
            else
            {
                FreeTick(dt);
            }
        }

        /// <summary>
        /// How far above their ground the fight is (half the higher one's height, in a bout): a camera
        /// following a fight in the sky keeps the ground and the fighters both in view.
        /// </summary>
        private float Lift
        {
            get
            {
                if (_stage != Stage.Bout)
                {
                    return 0f;
                }
                float ha = _ma != null && !_ma.Finished && !_goneA ? _ma.Pose.Height : 0f;
                float hb = _mb != null && !_mb.Finished && !_goneB ? _mb.Pose.Height : 0f;
                return Mathf.Clamp(Mathf.Max(ha, hb) * 0.5f, 0f, 6f);
            }
        }

        private void UpdateMid()
        {
            Vector2 a = _goneA ? PosB : PosA, b = _goneB ? PosA : PosB;
            Mid = (a + b) * 0.5f;
            Visible = Kit.Visible(Mid, 0.4f);
        }

        /// <summary>Still the same unit, alive, on the map and free of the god hand, buildings and possession.</summary>
        private static bool Valid(Actor a, long id)
        {
            return a != null && a.Sid() == id && a.isAlive() && a.hasHealth() && !a.isInMagnet() && !a.isInsideSomething()
                && !ControllableUnit.isControllingUnit(a);
        }

        private static bool Alive(Actor a, long id)
        {
            return a != null && a.Sid() == id && a.isAlive() && a.hasHealth();
        }

        private static bool Running(Duelist m, Actor a)
        {
            return m != null && !m.Finished && Moves.TryGet(a, out Move cur) && cur == m;
        }

        private void BoutTick(float dt)
        {
            // a held fighter's move vanished under us (an error, a cleared world)
            if ((!_goneA && !Running(_ma, A)) || (!_goneB && !Running(_mb, B)) || _ex == null)
            {
                End("lost a fighter's move", false);
                return;
            }
            if (!_goneA && !_goneB)
            {
                Director.Claim(this, 2.5f, A, B);
            }
            if (_ma != null)
            {
                _ma.Idle = 0f;
            }
            if (_mb != null)
            {
                _mb.Idle = 0f;
            }
            _ex.Step(dt);
            if (_ex is Catastrophe c && c.Follow)
            {
                Pan();
            }
            if (_ex.Done)
            {
                AfterExchange();
            }
        }

        /// <summary>
        /// "Camera follows god abilities": while a catastrophe carries the fight across the map, the
        /// camera glides after it (never otherwise: the camera is the player's).
        /// </summary>
        private void Pan()
        {
            try
            {
                MoveCamera mc = World.world != null ? World.world.move_camera : null;
                if (mc == null || MoveCamera.hasFocusUnit())
                {
                    return;   // the player is following a unit: that is its camera
                }
                Transform t = mc.transform;
                Vector3 p = t.position;
                Vector2 want = Mid + new Vector2(0f, 1.5f * K + Lift);
                float k = 1f - Mathf.Exp(-3.5f * Mathf.Min(0.1f, Time.unscaledDeltaTime));
                t.position = new Vector3(Mathf.Lerp(p.x, want.x, k), Mathf.Lerp(p.y, want.y, k), p.z);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Sakuga] duel camera: " + e.Message);
            }
        }

        private void FreeTick(float dt)
        {
            if (!Settings.Duels || T > MaxLength)
            {
                End(Settings.Duels ? "ran its course" : "duels off", Settings.Duels);
                return;
            }
            if (!Fought)
            {
                // until the first bout starts, the Brain keeps its hands off them
                Director.Claim(this, 1f, A, B);
                if (T > 4f)
                {
                    End("never got going");
                    return;
                }
            }
            if (!Foes())
            {
                End("no longer foes", false);
                return;
            }
            // the game's own "run away" (a whim of the unit's mind, or a hard landing at low health)
            // would walk out of the duel: a duelist stands its ground, only the Final ends it
            Stand(A, B);
            Stand(B, A);
            Footing(dt);
            float d = (A.current_position - B.current_position).magnitude;
            if (d > (Grand ? 22f : 12f))
            {
                _far += dt;
                if (_far > (Grand ? 6f : 4f))
                {
                    End("too far apart");
                    return;
                }
            }
            else
            {
                _far = 0f;
            }
            _aim -= dt;
            if (_aim <= 0f)
            {
                _aim = 0.3f;
                Aim(A, B);
                Aim(B, A);
                if (Fought && _free > 0.35f && Last != "final" && FinalDue())
                {
                    _free = 0.35f;   // one of them is at the end of its strength: the Final comes now
                    _window = Mathf.Min(_window, 0.35f);
                }
            }
            Looks();
            if (Flow && Fought && _window > 0f && Last != "final")
            {
                // a master's free fighting is a breath for a technique: once each one with a technique
                // to spend has started it, or has looked and found none worth it (or neither has one
                // to spend), the breath is over: no standing about waiting for nothing
                bool waitA = _allowA > 0 && _usedA == 0 && !_lookedA;
                bool waitB = _allowB > 0 && _usedB == 0 && !_lookedB;
                if (!waitA && !waitB)
                {
                    _window = 0f;
                    _free = Mathf.Min(_free, 0.12f);
                }
            }
            _window -= dt;
            Answers(dt);
            if (_window <= 0f && Fought && !FinalDue())
            {
                // the free fighting is over: the next bout starts the moment both are free. Whoever
                // has not had its turn yet answers the other's technique first (an exchange of
                // techniques, not one fighter stringing them onto the other). A technique still
                // playing out is simply waited for; no new ones are handed out meanwhile.
                Answer(A, true);
                Answer(B, false);
                if (Pressing(A) || Pressing(B) || _ansA > 0f || _ansB > 0f)
                {
                    _waited = 0f;
                }
                else
                {
                    // nobody mid-technique and still no bout: they are apart, or one is reeling (in
                    // the air, stunned, swimming). Two on their feet get the free fighting back soon
                    // (a master doesn't stand about); one reeling gets time to land first
                    _waited += dt;
                    bool up = Ready(A) && Ready(B);
                    float need = (up ? (Flow ? 0.6f : Low == Tier.Expert ? 1.5f : 3f) : (Flow ? 2f : 3.5f)) * (1f + 0.5f * _refills);
                    if (_waited > need)
                    {
                        _waited = 0f;
                        _refills++;
                        _window = FreeTime();
                        Ration();
                        Duels.Trace("duel " + IdA + "/" + IdB + " the next bout waits (" + (up ? "apart" : "one reeling") + "): free fighting again, techniques " + _allowA + "/" + _allowB);
                    }
                }
            }
            _free -= dt;
            if (_free > 0f)
            {
                return;
            }
            TryBout(d);
        }

        private bool Foes()
        {
            return A.kingdom != null && B.kingdom != null && A.areFoes(B);
        }

        /// <summary>A duelist told to run away by the game turns back to its opponent.</summary>
        private void Stand(Actor a, Actor o)
        {
            if (!a.isTask("run_away"))
            {
                return;
            }
            a.cancelAllBeh();
            a.setAttackTarget(o);
            Duels.Trace("duel " + IdA + "/" + IdB + ": " + a.asset.id + "#" + a.Sid() + " stands its ground (hp " + Combat.HpPct(a).ToString("F2") + ")");
        }

        /// <summary>Free to fight a bout: may act, not stunned, in no other scene.</summary>
        public bool Ready(Actor a)
        {
            if (!Moves.CanPerform(a) || a.hasStatusStunned())
            {
                return false;
            }
            object s = Director.SceneOf(a);
            return s == null || s == this;
        }

        /// <summary>
        /// Just after a bout, a duelist standing in a fresh crater that burns (or anywhere it may not
        /// stand) hops out to safe ground before it does anything else (the hop waits for its move
        /// to be let go, at most half a second).
        /// </summary>
        private void Footing(float dt)
        {
            if (!_footA && !_footB)
            {
                return;
            }
            _footFor -= dt;
            if (_footFor <= 0f)
            {
                _footA = _footB = false;
                return;
            }
            if (_footA && !_goneA && Moves.CanPerform(A))
            {
                _footA = false;
                Vault.Begin(A, B.current_position);
            }
            if (_footB && !_goneB && Moves.CanPerform(B))
            {
                _footB = false;
                Vault.Begin(B, A.current_position);
            }
        }

        private static void Aim(Actor a, Actor b)
        {
            if (a.attack_target != b)
            {
                a.setAttackTarget(b);
            }
        }

        // -------------------------------------------------------------- bouts --

        private void TryBout(float dist)
        {
            // the opening plays from where they met (a stare-down walks them in); later bouts need them
            // close (masters blur across a few tiles to the next exchange rather than walk)
            float reach = _queue.Count > 0 ? Duels.Range + 0.8f : Gods ? 13f : Flow ? 10f : 7.5f;
            if (!Ready(A) || !Ready(B) || dist > reach)
            {
                _free = Flow ? 0.08f : 0.15f;   // one is busy, stunned or away: soon
                return;
            }
            Refresh();
            Exchange ex;
            if (_queue.Count > 0)
            {
                // the opening waits for room (they are walking at each other) rather than being skipped
                ex = TryPlan(_queue[0]);
                if (ex == null)
                {
                    _free = 0.25f;
                    return;
                }
                _queue.RemoveAt(0);
            }
            else
            {
                ex = Pick(false);
            }
            if (ex == null)
            {
                _free = Flow ? 0.15f : 0.3f;
                return;
            }
            if (!Director.Claim(this, 3f, A, B))
            {
                _free = Flow ? 0.2f : 0.4f;
                return;
            }
            _ma = new Duelist(A, this, LvA);
            _mb = new Duelist(B, this, LvB);
            if (!Moves.Start(_ma))
            {
                _ma = _mb = null;
                _free = 0.2f;
                return;
            }
            if (!Moves.Start(_mb))
            {
                _ma.Let();
                _ma = _mb = null;
                _free = 0.2f;
                return;
            }
            _stage = Stage.Bout;
            _bout = 0;
            _waited = 0f;
            Run(ex);
        }

        private Exchange TryPlan(Exchange ex)
        {
            ex.Bind(this);
            return ex.Plan() ? ex : null;
        }

        private void Run(Exchange ex)
        {
            ex.P = _ma;
            ex.Q = _mb;
            _ex = ex;
            _bout++;
            ex.Start();
            ex.Step(0f);
            if (History.Count < 64)
            {
                History.Add(ex.Id);
            }
            Reads.Exchanged(A, B, ex.Forms, ex.Id);
            try
            {
                Hooks.ExchangeStarted?.Invoke(this, ex);
            }
            catch (System.Exception e)
            {
                SakugaMod.Report("exchange hook", e);
            }
            Duels.Trace("duel " + IdA + "/" + IdB + " " + ex.Id + " (L" + L + ", hp " + HpA.ToString("F2") + "/" + HpB.ToString("F2") + ", t " + T.ToString("F1") + ")");
        }

        private void AfterExchange()
        {
            Exchange done = _ex;
            _ex = null;
            if (!(done is Recover))
            {
                Fought = true;
                Exchanges++;
                Prev2 = Prev;
                Prev = Last;
                Last = done.Id;
            }
            if (_ending || _goneA || _goneB)
            {
                End("one is gone");
                return;
            }
            if (done is IFinale f)
            {
                // (a God's Last Light may cancel the blow that should have ended it: two Gods get a third)
                if (f.Killed || ++_finals >= (Gods ? 3 : 2))
                {
                    End(f.Killed ? "the Final" : "two Finals and both stand");   // settled, or two decisive cuts and both still stand: rivals
                    return;
                }
                // both on one knee (or it lived through it): a long breath, and on
                ReleaseBout(null);
                Free(Flow ? Random.Range(1f, 1.4f) : Random.Range(2f, 3f));
                return;
            }
            if (done is SignatureBout)
            {
                // its tell is answered by the great technique itself, forced at the release (ReleaseBout):
                // nothing may chain in between, or the tell is left without its technique
                ReleaseBout(done);
                Free(FreeTime());
                return;
            }
            Exchange next = null;
            if (SpiralPending)
            {
                SpiralPending = false;
                Spirals++;
                next = TryPlan(new ClashSpiral());
            }
            while (next == null && _queue.Count > 0)
            {
                Exchange q = _queue[0];
                _queue.RemoveAt(0);
                next = TryPlan(q);
            }
            if (next == null && done is Circle && Settings.Duels && T < MaxLength)
            {
                next = Pick(true);   // the breath leads straight into a fight
            }
            // (_bout counts the exchanges already run in this bout: up to MaxBout in all)
            if (next == null && done.Intense && _bout < MaxBout && Settings.Duels && T < MaxLength && !FinalDue()
                && Random.value < ChainChance(done))
            {
                next = Pick(false);   // the fight flows straight on into another (or a breath)
            }
            if (next != null)
            {
                Run(next);
                return;
            }
            ReleaseBout(done);
            Free(FreeTime());
        }

        /// <summary>How likely a fight flows straight on into the next exchange (the higher the tiers, the likelier).</summary>
        private float ChainChance(Exchange done)
        {
            Tier low = Low;
            float c = low >= Tier.God ? 0.55f : low == Tier.Grandmaster ? 0.55f : low == Tier.Master ? 0.5f : low == Tier.Expert ? 0.3f : 0.18f;
            return done is Catastrophe ? c + 0.1f : c;
        }

        /// <summary>
        /// The free fighting between bouts: shorter the higher the tiers. Masters and up barely let go
        /// of each other (a breath for a technique or a strike), Experts about a second.
        /// </summary>
        private float FreeTime()
        {
            Tier low = Low;
            return low >= Tier.God ? Random.Range(0.3f, 0.5f)
                : low == Tier.Grandmaster ? Random.Range(0.35f, 0.6f)
                : low == Tier.Master ? Random.Range(0.45f, 0.8f)
                : low == Tier.Expert ? Random.Range(0.8f, 1.2f)
                : Random.Range(1.4f, 2.2f);
        }

        private void Free(float seconds)
        {
            _stage = Stage.Free;
            _free = seconds;
            _window = seconds;
            _waited = 0f;
            _refills = 0;
            _aim = 0f;
            Ration();
            if (_followUp != 0 && Fought)
            {
                // the winner of the bout was handed its follow-up: that technique is its share
                if (_followUp == IdA && _allowA == 0)
                {
                    _allowA = 1;
                }
                else if (_followUp == IdB && _allowB == 0)
                {
                    _allowB = 1;
                }
            }
            _followUp = 0;
            if (Dev.On && Fought)
            {
                Duels.Trace("duel " + IdA + "/" + IdB + " free " + seconds.ToString("F1") + " s, techniques " + _allowA + "/" + _allowB
                    + " (hp " + HpA.ToString("F2") + "/" + HpB.ToString("F2") + ", t " + T.ToString("F1") + ")");
            }
        }

        /// <summary>
        /// Hands both back to the game (and the Brain) between bouts, aimed at each other. The
        /// winner of the last exchange (Exchange.Upper) is handed a chain link onto the loser, so
        /// its technique follows the exchange at once, as one move (Experts and up).
        /// </summary>
        private void ReleaseBout(Exchange done)
        {
            Vector2 pa = PosA, pb = PosB;
            _ma?.Let();
            _mb?.Let();
            _ma = _mb = null;
            _stage = Stage.Free;
            Director.Release(this);
            _lead = done != null && done.Upper != null ? done.Upper.Id : 0;
            _followUp = 0;
            if (!_goneA && !_goneB && Valid(A, IdA) && Valid(B, IdB))
            {
                Aim(A, B);
                Aim(B, A);
                Think(A);
                Think(B);
                if (done != null && done.Upper != null && done.Opening != Link.None && Low >= Tier.Expert)
                {
                    Duelist w = done.Upper;
                    Actor l = w.Id == IdA ? B : A;
                    if ((pa - pb).sqrMagnitude <= 3f * 3f * K * K && Fighters.TryGet(w.A, out Fighter f))
                    {
                        f.Link = done.Opening;
                        f.LinkTarget = l;
                        f.LinkUntil = Fighter.Now + 0.5;
                        f.ThinkIn = 0f;
                        _followUp = w.Id;
                        Duels.Trace("duel " + IdA + "/" + IdB + " " + w.A.asset.id + "#" + w.Id + " follows up (" + done.Opening + ")");
                    }
                }
                _footA = _footB = true;
                _footFor = 0.5f;
                if (done is SignatureBout sb && sb.Sig != null && Valid(sb.Winner, sb.Winner.Sid()) && Combat.Living(sb.Loser))
                {
                    _footA = _footB = false;
                    // the winner's Duelist was just let go (Let) but holds it until the next Moves.Update:
                    // ended now, or every forced technique found it Committed and the tell played for nothing
                    if (Moves.TryGet(sb.Winner, out Move held) && held is Duelist)
                    {
                        Moves.EndFinished(held);
                    }
                    // (the first of its great techniques that will really play here: a tell is never left without its answer)
                    bool ok = false;
                    string used = sb.Sig.Id;
                    foreach (var kv in sb.Candidates)
                    {
                        if (Brain.Force(sb.Winner, kv.Key, kv.Value, sb.Loser))
                        {
                            ok = true;
                            used = kv.Key.Id;
                            break;
                        }
                    }
                    Duels.Trace("duel " + IdA + "/" + IdB + " signature " + used + " by " + sb.Winner.asset.id + ": " + ok);
                }
            }
        }

        private void Think(Actor a)
        {
            if (Fighters.TryGet(a, out Fighter f))
            {
                f.ThinkIn = Mathf.Min(f.ThinkIn, Flow ? Random.Range(0f, 0.08f) : Random.Range(0.05f, 0.35f));
            }
        }

        // -------------------------------------------------------------- pick --

        /// <summary>
        /// The next exchange: the Final if one of them is near the end, otherwise one from the pool,
        /// by weights from the circumstances, never the last one again (and the one before less likely).
        /// </summary>
        private Exchange Pick(bool chained)
        {
            if (FinalDue())
            {
                // one of the endings (Finales: the dash-through, the last clash, the sky finish, the
                // overwhelm, the winner's school's own art), never the one a fighter ended its last duel on
                Exchange f = null;
                try
                {
                    f = Finales.Choose(this);
                }
                catch (System.Exception e)
                {
                    SakugaMod.Report("duel finale", e);
                }
                f = f ?? TryPlan(new Final());
                if (f != null)
                {
                    return f;
                }
            }
            _pick.Clear();
            _weights.Clear();
            Exchange[] pool = Grand
                ? new Exchange[]
                {
                    new BladeLock(), new FlurryExchange(), new AerialClash(), new Chase(), new GroundShaker(), new Feint(), new Circle(),
                    new Heavens(true), new Skyfall(), new Tectonic(), new SkyBattle(), new MountainBreaker(), new Relentless(), new SignatureBout(),
                    new Salvo(), new AuraClash(), new Blitz()
                }
                : new Exchange[]
                {
                    new BladeLock(), new FlurryExchange(), new AerialClash(), new Chase(), new GroundShaker(), new Feint(), new Circle(),
                    new Heavens(true), new SignatureBout(), new Salvo(), new AuraClash(), new Blitz()
                };
            string forced = Duels.TakeForced();
            if (forced != null)
            {
                foreach (Exchange ex in pool)
                {
                    if (ex.Id == forced && TryPlan(ex) != null)
                    {
                        return ex;
                    }
                }
            }
            float total = 0f;
            foreach (Exchange ex in pool)
            {
                if (ex.Id == Last || (chained && ex is Circle))
                {
                    continue;
                }
                ex.Bind(this);
                float w = ex.Weight();
                if (w <= 0f)
                {
                    continue;
                }
                // the duelists' own tastes (their personas) and any later hooks (Duels.Weighers)
                w *= Duels.Taste(this, ex);
                if (Gods && !(ex is Catastrophe))
                {
                    w *= 0.7f;   // two Gods: the blades still cross, but the world is their stage
                }
                if (ex.Id == Prev)
                {
                    w *= 0.5f;
                }
                else if (ex.Id == Prev2)
                {
                    w *= 0.75f;
                }
                int seen = 0;
                for (int h = 0; h < History.Count; h++)
                {
                    if (History[h] == ex.Id)
                    {
                        seen++;
                    }
                }
                if (seen > 0 && !(ex is Circle))
                {
                    // (a catastrophe twice in one duel is already a lot)
                    w *= Mathf.Pow(ex is Catastrophe ? 0.3f : 0.45f, seen);
                }
                if (!ex.Plan())
                {
                    continue;
                }
                _pick.Add(ex);
                _weights.Add(w);
                total += w;
            }
            if (_pick.Count == 0)
            {
                // cramped ground where only the last one fits: better that again than nothing
                foreach (Exchange ex in pool)
                {
                    if (ex.Id == Last && !(chained && ex is Circle) && TryPlan(ex) != null && ex.Weight() > 0f)
                    {
                        return ex;
                    }
                }
                return null;
            }
            float r = Random.value * total;
            for (int i = 0; i < _pick.Count; i++)
            {
                r -= _weights[i];
                if (r <= 0f)
                {
                    return _pick[i];
                }
            }
            return _pick[_pick.Count - 1];
        }

        /// <summary>
        /// One of them is below a quarter of its health and has no Second Wind left to come back with,
        /// or the duel has run its course (both still strong: one decisive cut settles it).
        /// </summary>
        /// <summary>Seconds of free fighting left before the next bout (FlowStrikes reads it).</summary>
        public float FreeLeft => _free;

        /// <summary>The final exchange is due (FlowStrikes reads it).</summary>
        public bool AtFinal => FinalDue();

        private bool FinalDue()
        {
            if (Exchanges < 2)
            {
                return false;
            }
            if ((HpA < 0.25f && WindSpent(A)) || (HpB < 0.25f && WindSpent(B)))
            {
                return true;
            }
            return T >= _finalAt && Exchanges >= 3;
        }

        /// <summary>The duel ran its course with both still standing strong (the Final may then end in both kneeling).</summary>
        public bool Timed => T >= _finalAt && HpA >= 0.25f && HpB >= 0.25f;

        /// <summary>No Second Wind left to come back with: never had one, or it rose and had its few seconds.</summary>
        private static bool WindSpent(Actor a)
        {
            return Tiers.Of(a) < Tier.Expert || !Fighters.TryGet(a, out Fighter f) || f.SecondWindUsed && Presence.SinceWind(a) > 4f;
        }

        // ------------------------------------------------------------- clash --

        /// <summary>
        /// The clash rule's roll: Power = PR x (0.75 + 0.5 random) x (0.5 + 0.5 HP%) for each
        /// (plus a little momentum for whoever won the last clash). Under 1.25 even, 1.25-3 a win,
        /// 3 or more overwhelming. No side effects.
        /// </summary>
        public Clash Roll()
        {
            float m = Mathf.Clamp(_momentum, -1f, 1f);
            float pa = PowerA * Random.Range(0.75f, 1.25f) * (0.5f + 0.5f * HpA) * (1f + 0.08f * m);
            float pb = PowerB * Random.Range(0.75f, 1.25f) * (0.5f + 0.5f * HpB) * (1f - 0.08f * m);
            float ratio = Mathf.Max(pa, pb) / Mathf.Max(0.001f, Mathf.Min(pa, pb));
            return new Clash
            {
                AWins = pa >= pb,
                Ratio = ratio,
                Kind = ratio < 1.25f ? ClashKind.Even : ratio < 3f ? ClashKind.Win : ClashKind.Overwhelm
            };
        }

        /// <summary>
        /// A clash inside an exchange. 'decisive': the clash that decides the exchange (it counts
        /// toward the streak: three even ones in a row and the next thing is a Clash Spiral).
        /// </summary>
        public Clash Resolve(bool decisive)
        {
            return Count(Roll(), decisive);
        }

        /// <summary>
        /// A clash rolled ahead (Roll, when an exchange's plan depends on who wins) now happens: it
        /// counts, it swings the momentum, it adds to the even streak.
        /// </summary>
        public Clash Count(Clash c, bool decisive)
        {
            Clashes++;
            if (!c.Even)
            {
                _momentum = Mathf.Clamp(_momentum + (c.AWins ? 0.6f : -0.6f), -1f, 1f);
            }
            if (decisive)
            {
                if (c.Even)
                {
                    EvenStreak++;
                    if (EvenStreak >= 3)
                    {
                        EvenStreak = 0;
                        SpiralPending = Spirals < 2;
                    }
                }
                else
                {
                    EvenStreak = 0;
                }
            }
            return c;
        }

        /// <summary>
        /// Chip damage: a share of the loser's own health (mult 1 = 12%, a little less in the long
        /// duels; whatever their tiers, an exchange lost costs the same, so the tiers' Might is taken
        /// back out of it), boosted by the winner's chains and Second Wind, never lethal and never
        /// under the loser's floor (duels end on the Final), half against a heavier class
        /// (Combat.Allowed). The loser flashes (its own tier colour when hard) even when its floor
        /// takes the blow.
        /// </summary>
        public void Chip(Duelist w, Duelist l, float mult, bool heavy)
        {
            if (w == null || l == null || !Alive(w.A, w.Id) || !Alive(l.A, l.Id) || mult <= 0f)
            {
                return;
            }
            if (!Combat.Allowed(w.A, l.A, out float cm))
            {
                cm = 0.5f;   // punching above its weight: a glancing blow
            }
            float armor = Mathf.Min(l.A.stats["armor"], 95f);
            float through = Mathf.Max(0.05f, 1f - armor / 100f);
            float take = ChipFrac * mult * cm * Random.Range(0.85f, 1.15f) * l.A.getMaxHealth();
            int hp = l.A.getHealth();
            take = Mathf.Min(take, hp - FloorHp(l.A));
            // exactly what was worked out, never lethal: it goes past Combat.Hit, whose critical roll
            // (twice the blow for a God) would carry a chip already cut at the floor far under it
            float d = Mathf.Min(take / through, (hp - 1) / through - 0.5f);
            if (take >= 1f && d >= 1f)
            {
                Duels.OwnBlow++;
                try
                {
                    l.A.getHit(d, true, AttackType.Other, w.A, false, false, true);
                }
                finally
                {
                    Duels.OwnBlow--;
                }
                HitResult r = new HitResult { Landed = true, Dealt = Mathf.Min(hp, d * through), Killed = !l.A.hasHealth() };
                try
                {
                    Hooks.HitDealt?.Invoke(w.A, l.A, r, w);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Sakuga] hit hook: " + e);
                }
            }
            Tier lt = Tiers.Of(l.A);
            l.Flash(heavy ? 0.08f : 0.05f, heavy && lt != Tier.None ? Tiers.Color(lt) : Color.white);
            // the blow carries the winner's school (a fist's print, a cut of light, a burst of ki...)
            SchoolStyle.Stamp(w.A, l.A, w, heavy ? 1.4f : 0.8f);
        }

        /// <summary>The last blow of the Final Exchange: enough to end it, through any armor.</summary>
        public HitResult FinishingBlow(Duelist w, Duelist l)
        {
            if (w == null || l == null || !Alive(l.A, l.Id) || !w.A.isAlive())
            {
                return default;
            }
            _killer = w.A.Sid();
            _victim = l.A.Sid();
            float armor = Mathf.Min(l.A.stats["armor"], 95f);
            float through = Mathf.Max(0.05f, 1f - armor / 100f);
            // Combat.Hit multiplies it by the winner's boosts and Might and the Power setting: a low
            // Power setting must not let the decisive cut fall short
            float flat = ((l.A.getHealth() + 2f) / through + 2f) / Mathf.Min(1f, Boost(w.A));
            Duels.OwnBlow++;
            try
            {
                return Combat.Hit(w.A, l.A, 0f, true, true, flat, w);
            }
            finally
            {
                Duels.OwnBlow--;
            }
        }

        // ------------------------------------------------------------- stage --

        /// <summary>Untiered bystanders within 2 tiles of either fighter step back a tile: the crowd parts.</summary>
        public void PartCrowd()
        {
            WorldTile t = Combat.TileAt(Mid);
            if (t == null)
            {
                return;
            }
            Vector2 pa = PosA, pb = PosB;
            List<Actor> near = new List<Actor>();
            foreach (Actor u in Finder.getUnitsFromChunk(t, 1))
            {
                near.Add(u);
            }
            int n = 0;
            foreach (Actor u in near)
            {
                if (n >= 10)
                {
                    break;
                }
                if (u == null || u == A || u == B || !u.isAlive())
                {
                    continue;
                }
                float da = (u.current_position - pa).sqrMagnitude, db = (u.current_position - pb).sqrMagnitude;
                if (Mathf.Min(da, db) > 4f)
                {
                    continue;
                }
                if (Sidestep.Begin(u, da < db ? pa : pb, Mid))
                {
                    n++;
                }
            }
        }

        public void Sound(string id)
        {
            Anim.Sound(id, Combat.TileAt(Mid));
        }

        /// <summary>A cinematic edge (darkened corners) and a title card, when the world may hold one.</summary>
        public void Cine(Vector2 at)
        {
            Cinema.Title(Locale.Get("sakuga_duel_heavens"), Tiers.Color(Tier.God), 2.2f, at, 0.8f);
            if (_cine || Settings.Cinematics < 2 || !Cinema.FullAt(at))
            {
                return;
            }
            if (Cinema.TryBegin(this, 8f))
            {
                _cine = true;
                Cinema.Dim(0.32f, Color.black, 0.25f, true);
            }
        }

        public void EndCine()
        {
            if (_cine)
            {
                _cine = false;
                Cinema.End(this);
            }
        }

        // --------------------------------------------------------------- end --

        /// <summary>One of them is gone (dead, taken by the god hand, possessed, inside something).</summary>
        private void Lose(bool aSide)
        {
            if (aSide)
            {
                _goneA = true;
            }
            else
            {
                _goneB = true;
            }
            if (_stage == Stage.Bout && _ex != null && _ex.Solo)
            {
                return;   // the Final's winner finishes its beat; the loser is the game's now
            }
            Actor x = aSide ? A : B;
            Duelist m = aSide ? _ma : _mb;
            bool dying = x != null && x.Sid() == (aSide ? IdA : IdB) && x.isAlive() && !x.hasHealth();
            if (m != null && !m.Finished && !dying)
            {
                m.Let();
            }
            if (_goneA && _goneB)
            {
                End("both gone");
                return;
            }
            if (_stage == Stage.Bout)
            {
                Duelist s = aSide ? _mb : _ma;
                // where the survivor stood when the broken exchange began (safe ground, if all else
                // fails), and whether it had vanished mid-move (the abort shows it again)
                Vector2 home = _ex != null ? _ex.Home(!aSide) : aSide ? PosB : PosA;
                bool vanished = s != null && s.Pose.Hidden;
                _ex?.Abort();
                _ex = null;
                if (s != null && !s.Finished)
                {
                    // the survivor comes down and finds its feet before it's let go
                    Recover r = new Recover(s, home, vanished);
                    r.Bind(this);
                    _ex = r;
                    _ending = true;
                    r.Start();
                    r.Step(0f);
                    return;
                }
            }
            End((aSide ? "A" : "B") + " is gone");
        }

        /// <summary>Called by a Duelist being dropped (died, picked up, the world cleared): the next tick sorts it out.</summary>
        public void MoveLost(Duelist m)
        {
            if (!Over)
            {
                Duels.Trace("duel " + IdA + "/" + IdB + " lost a fighter's move");
            }
        }

        /// <summary>A unit was killed (Hooks.Killed): by the other duelist, that's the duel won.</summary>
        public void Died(Actor victim, Actor killer)
        {
            if (Over || Awarded || victim == null || killer == null)
            {
                return;
            }
            long v = victim.Sid(), k = killer.Sid();
            if ((v == IdA && k == IdB) || (v == IdB && k == IdA))
            {
                Awarded = true;
                Progression.DuelWon(killer, victim);
                Duels.Trace("duel won by " + killer.asset.id + "#" + k);
            }
        }

        /// <summary>
        /// Ends the duel: lets both go, settles who won or who became rivals. 'parting' false: it was
        /// called off (duels switched off, peace, a broken bout), so two left standing don't walk
        /// away from it as rivals.
        /// </summary>
        public void End(string why = null, bool parting = true)
        {
            if (Over)
            {
                return;
            }
            Over = true;
            Duels.Remember(this);
            if (why != null)
            {
                _why = why;
            }
            _ex?.Abort();
            _ex = null;
            _ma?.Let();
            _mb?.Let();
            _ma = _mb = null;
            _queue.Clear();
            Director.Release(this);
            EndCine();
            if (!Awarded && _killer >= 0)
            {
                Actor w = _killer == IdA ? A : _killer == IdB ? B : null;
                Actor l = _victim == IdA ? A : _victim == IdB ? B : null;
                if (w != null && Alive(w, _killer) && l != null && l.Sid() == _victim && (!l.isAlive() || !l.hasHealth()))
                {
                    Awarded = true;
                    Progression.DuelWon(w, l);
                }
            }
            bool both = Alive(A, IdA) && Alive(B, IdB);
            if (Fought && both && !Awarded && parting)
            {
                // rivals once they have really walked away from it (a blow a moment later settles it instead)
                Duels.Parted(A, B);
            }
            Duels.Rest(IdA);
            Duels.Rest(IdB);
            Duels.Trace("duel " + IdA + "/" + IdB + " over after " + T.ToString("F1") + " s, " + Exchanges + " exchanges, " + Clashes + " clashes"
                + (Awarded ? ", won" : both ? ", both stand" : "") + (_why.Length > 0 ? " (" + _why + ")" : ""));
        }

        /// <summary>Something threw: let everything go without asking anything more of it.</summary>
        public void Abandon()
        {
            try
            {
                End();
            }
            catch
            {
                Over = true;
                _ma?.Let();
                _mb?.Let();
                Director.Release(this);
                EndCine();
            }
        }
    }
}
