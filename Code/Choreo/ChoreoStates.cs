using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    internal partial class ChoreoMove
    {
        // ------------------------------------------------------ answered blows --

        /// <summary>0: none; 1: parried (it reels back); 2: evaded (it overreaches into empty air).</summary>
        private int _answer;
        private float _aT;
        private Vector2 _aFrom, _aTo;

        /// <summary>The main target answered the blow: the script stops here and the move ends in a recoil or a whiff.</summary>
        private void Answered(bool parried)
        {
            if (_answer != 0 || _broken)
            {
                return;
            }
            _answer = parried ? 1 : 2;
            _aT = 0f;
            _aFrom = Pose.Ground;
            _link = Link.None;
            _linkTarget = null;
            _hold = 0f;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (Held != null)
            {
                ReleaseHeld(Hurt.Reel, 0.5f);
            }
            Vector2 to = parried ? _aFrom - Dir * 0.38f * Kk : _aFrom + Dir * 0.25f * Kk;
            _aTo = Combat.SafeSpot(to) && Combat.ClearPath(_aFrom, to) ? to : _aFrom;
            _rotFrom = Mathf.DeltaAngle(0f, Pose.Rot);
            _hFrom = Pose.Height;
            if (parried)
            {
                Choreo.Sound(Armed ? "event:/SFX/HIT/HitSwordSword" : "event:/SFX/HIT/HitMetal", A.current_tile);
                if (Vis)
                {
                    Vector2 at = Vector2.Lerp(Hand(Limb.Fist), VPoint(0.6f), 0.5f);
                    Fx.Glint(at, Px, 0.16f, new Color(1f, 0.92f, 0.55f, 1f), 0.9f);
                    Kit.Sparks(at, Px, 5, 40f, 90f, new Color(1f, 0.9f, 0.6f, 1f));
                    Fx.Afterimage(this, 0.12f, new Color(1f, 1f, 1f, 0.7f), Hue.GhostEnd);
                }
            }
            else
            {
                Choreo.Sound("event:/SFX/UNIQUE/SpawnWhoosh", A.current_tile);
                if (Vis)
                {
                    Particle p = Fx.Spawn(Art.Arc(7, 0.8f, 60f, -60f, 2), Hand(Limb.Fist) + new Vector2(Fwd * 2f * Px, 0f), Px, 0.14f, FxLayer.Over, 5);
                    if (p != null)
                    {
                        p.SX = Fwd;
                        p.Color(new Color(1f, 1f, 1f, 0.8f), new Color(1f, 1f, 1f, 0.5f), new Color(1f, 1f, 1f, 0f));
                    }
                }
            }
            if (Dev.On) Dev.Trace("choreo " + Id + (parried ? " parried: it reels back" : " evaded: it overreaches"));
        }

        private void TickAnswer(float dt)
        {
            _aT += dt;
            if (_answer == 1)
            {
                float dur = 0.32f * Mathf.Max(0.6f, RecoverK);
                float k = Mathf.Clamp01(_aT / dur);
                Pose.Ground = Vector2.Lerp(_aFrom, _aTo, Beat.Apply(Ease.OutCubic, Mathf.Clamp01(k * 2f)));
                float lean = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k * 1.25f));
                Pose.Rot = Mathf.Lerp(_rotFrom, 0f, Mathf.Clamp01(k * 4f)) - 20f * lean;
                Pose.SX = 1f - 0.06f * lean;
                Pose.SY = 1f + 0.06f * lean;
                Pose.Height = Mathf.Lerp(_hFrom, 0f, Mathf.Clamp01(k * 3f));
                Pose.Nudge = k < 0.55f ? Choreo.Tremble(_aT, 1f) : Vector2.zero;
                if (k >= 1f)
                {
                    EndAnswered();
                }
                return;
            }
            float d2 = 0.28f * Mathf.Max(0.6f, RecoverK);
            float k2 = Mathf.Clamp01(_aT / d2);
            float s = Beat.Apply(Ease.OutQuad, k2);
            Pose.Ground = Vector2.Lerp(_aFrom, _aTo, s);
            Pose.Rot = Mathf.Lerp(24f, 3f, s);
            Pose.SX = Mathf.Lerp(1.12f, 1f, s);
            Pose.SY = Mathf.Lerp(0.9f, 1f, s);
            Pose.Height = Mathf.Lerp(_hFrom, 0f, Mathf.Clamp01(k2 * 3f));
            Pose.Nudge = Vector2.zero;
            if (k2 >= 1f)
            {
                EndAnswered();
            }
        }

        private void EndAnswered()
        {
            Stance = Pose.Ground;
            Neutral();
            if (!Choreo.Flowing(Tier))
            {
                A.startAttackCooldown();
            }
            // the one that slipped it is right there: whatever follows a close-in exchange comes next
            if (_answer == 2 && V != null && V.Sid() == VId && Combat.Living(V) && Combat.IsFoe(A, V) && Combat.Grounded(V)
                && Vector2.Distance(Pose.Ground, V.current_position) <= 2.2f * Kk)
            {
                EndLink = Link.Close;
                EndTarget = V;
            }
            Cleanup(false);
            Finished = true;
        }

        // ------------------------------------------------------------- broken --

        private bool _broken;
        private float _brT, _brDur, _brApex, _brRot0, _brH0;
        private Vector2 _brFrom, _brTo;
        private int _brStage;
        private bool _brHeavy, _brDazed;
        private float _brGhost;

        /// <summary>Is the move being blown apart (Choreo.Break)?</summary>
        public bool Broken => _broken;

        /// <summary>
        /// Its blow was answered: it reels back (parried) or overreaches (evaded). Open to the counter
        /// (Interruptible), but its own Brain starts nothing until the reel is over (Hooks.HoldTechniques).
        /// </summary>
        public bool Reeling => _answer != 0 && !_broken && !Finished;

        /// <summary>
        /// A fast blow breaks the move (Choreo.Break calls it): if it was gathering power the power
        /// bursts out of it (motes scattering, a broken ring, its orb popping); whatever it held is let
        /// go, its beams collapse; then the performer is blasted 'force' tiles along 'dir', tumbling,
        /// crashes, and lies dazed. Heavy units only reel where they stand. True if it was broken.
        /// </summary>
        public bool BreakCharge(Actor by, Vector2 dir, float force)
        {
            if (Finished || Ended || _broken || !_started)
            {
                return false;
            }
            bool charging = Charging;
            Vector2 at = charging && _orb != null && _orb.Alive ? _orb.Pos : BodyPos();
            Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionSmall", A.current_tile);
            Choreo.Sound("event:/SFX/HIT/HitGeneric", A.current_tile);
            if (Vis)
            {
                BreakBurst(at, charging);
            }
            // whatever it held goes
            _answer = 0;
            _hold = 0f;
            if (Held != null)
            {
                ReleaseHeld(Hurt.Knockdown, 0.5f);
            }
            Cleanup(true);
            _cleaned = false;   // (the flight below still ends the move through Finish/OnEnd)
            _broken = true;
            _brT = 0f;
            _brStage = 0;
            _brFrom = Pose.Ground;
            _brH0 = Pose.Height;
            _brRot0 = Mathf.DeltaAngle(0f, Pose.Rot);
            _brHeavy = Weights.Heavy(A) || A.isFlying();
            float d = Mathf.Clamp(force, 0.5f, 12f);
            Vector2 want = Combat.Clamp(_brFrom + dir.normalized * d);
            _brTo = Combat.FindLanding(want, 3f, out Vector2 spot) ? spot : _brFrom;
            _brDur = 0.3f + 0.045f * Vector2.Distance(_brFrom, _brTo);
            _brApex = Mathf.Clamp(0.35f + 0.16f * d, 0.4f, 2.2f) * Mathf.Sqrt(Kk);
            _lost = true;
            _link = Link.None;
            _linkTarget = null;
            Pose.Hidden = false;
            Pose.HideShadow = false;
            if (Mathf.Abs(dir.x) > 0.1f)
            {
                Pose.FaceRight = dir.x < 0f;   // it faces the one that hit it
                Fwd = Pose.FaceRight ? 1f : -1f;
            }
            Kit.Shake(at, 0.15f, 0.006f, Kk);
            if (Dev.On) Dev.Trace("choreo " + Id + " BROKEN" + (charging ? " mid-charge" : "") + " by " + (by != null ? by.asset.id + "#" + by.Sid() : "?")
                + ", flies " + Vector2.Distance(_brFrom, _brTo).ToString("F1") + " tiles");
            return true;
        }

        /// <summary>The gathered power bursting out: a flash, a ring breaking into pieces, motes scattering.</summary>
        private void BreakBurst(Vector2 at, bool charged)
        {
            float px = Px;
            Kit.Flash(at, px, Mathf.RoundToInt(5f * Kk), 0.08f, Fx.WithA(Hue.Core, 1f));
            Particle ring = Fx.Spawn(Art.Ring(Mathf.RoundToInt(9f * Kk), 1, 1f, 7), at, px, 0.3f, FxLayer.Top, 6);
            if (ring != null)
            {
                ring.Sized(1f, 2.2f).Color(Fx.WithA(Hue.Core, 1f), Fx.WithA(Hue.Edge, 0.7f), Fx.WithA(Hue.Edge, 0f));
                ring.Spin = Random.Range(-200f, 200f);
            }
            int n = Fx.Count(charged ? 16 : 8);
            for (int i = 0; i < n; i++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector2 dd = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Particle p = Fx.Spawn(Art.Pixel, at + dd * 2f * px, px, Random.Range(0.25f, 0.5f), FxLayer.Top, 9);
                p?.Moving(dd * Random.Range(40f, 120f) * px, Random.Range(0f, 20f) * px, 3.5f, 60f * px).Sized(1.6f, 1f)
                    .Color(Color.white, Fx.WithA(Hue.Core, 0.9f), Fx.WithA(Hue.Edge, 0f));
            }
            Kit.Sparks(at, px, 6, 50f, 120f, Hue.Edge);
        }

        private void TickBroken(float dt)
        {
            _brT += dt;
            if (_brHeavy)
            {
                // too heavy to blow away: it reels where it stands
                float k = Mathf.Clamp01(_brT / 0.45f);
                float lean = Mathf.Sin(Mathf.PI * k);
                Pose.Rot = Mathf.Lerp(_brRot0, 0f, Mathf.Clamp01(k * 3f)) - 18f * lean;
                Pose.SX = 1f - 0.05f * lean;
                Pose.SY = 1f + 0.05f * lean;
                Pose.Height = Mathf.Lerp(_brH0, 0f, Mathf.Clamp01(k * 3f));
                Pose.Nudge = k < 0.6f ? Choreo.Tremble(_brT, 1f) : Vector2.zero;
                if (k >= 1f)
                {
                    Combat.Stagger(A, 0.4f);
                    Stance = Pose.Ground;
                    Neutral();
                    EndBroken();
                }
                return;
            }
            if (_brStage == 0)
            {
                // blown away, tumbling backwards
                float k = Mathf.Clamp01(_brT / _brDur);
                Pose.Ground = Vector2.Lerp(_brFrom, _brTo, Beat.Apply(Ease.OutQuad, k));
                Pose.Height = Mathf.Lerp(_brH0, 0f, k) + _brApex * 4f * k * (1f - k);
                Pose.Rot = _brRot0 - 470f * Beat.Apply(Ease.OutQuad, k);
                float fast = Mathf.Abs(1f - 2f * k);
                Pose.SX = 1f - 0.12f * fast;
                Pose.SY = 1f + 0.18f * fast;
                Pose.HideShadow = Pose.Height > 0.1f;
                Pose.Nudge = Vector2.zero;
                _brGhost -= dt;
                if (Vis && _brGhost <= 0f)
                {
                    _brGhost = 0.035f;
                    Fx.Afterimage(this, 0.13f, new Color(1f, 1f, 1f, 0.6f), Fx.WithA(Hue.Edge, 0f));
                }
                if (k >= 1f)
                {
                    _brStage = 1;
                    _brT = 0f;
                    _brRot0 = Mathf.DeltaAngle(0f, Pose.Rot);
                    Stance = Pose.Ground;
                    Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", A.current_tile);
                    if (Vis)
                    {
                        Fx.LandingPuff(Pose.Ground, Px, A.current_tile);
                        Kit.DustRing(Pose.Ground, Px, A.current_tile, 8, 32f, 0.45f, Kk);
                    }
                }
                return;
            }
            // it lies where it crashed, then the daze keeps it down
            float u = Mathf.Clamp01(_brT / 0.12f);
            float rot = Mathf.Lerp(_brRot0, -90f, Beat.Apply(Ease.OutCubic, u));
            bool canFlip = A.asset.can_flip;
            float phi = (Pose.FaceRight && canFlip ? -rot : rot) * Mathf.Deg2Rad;
            Pose.Rot = rot;
            Pose.Nudge = new Vector2(-Mathf.Sin(phi), Mathf.Cos(phi) - 1f) * Pose.PivotUp;
            Pose.Height = 0.08f * Kk * Mathf.Sin(Mathf.PI * Mathf.Clamp01(_brT / 0.14f));
            float amp = Anim.Spring(_brT, 12f, 22f);
            Pose.SX = 1f + 0.12f * amp;
            Pose.SY = 1f - 0.1f * amp;
            Pose.HideShadow = false;
            if (!_brDazed && _brT > 0.05f)
            {
                _brDazed = true;
                Choreo.Daze(A, 0.9f);
            }
            if (_brT > 0.16f && (A.is_unconscious || _brT > 0.5f))
            {
                try
                {
                    A._rotation_direction = rot < 0f ? RotationDirection.Left : RotationDirection.Right;
                }
                catch
                {
                    // the game picks the side
                }
                Pose.Height = 0f;
                Pose.SX = Pose.SY = 1f;
                EndBroken();
            }
        }

        private void EndBroken()
        {
            EndLink = Link.None;
            EndTarget = null;
            Cleanup(true);
            Finished = true;
        }

        // ---------------------------------------------------------- cinematic --

        /// <summary>
        /// Opens a cinematic for this move (Ultimate-scale techniques): only one plays at a time, with
        /// 'gap' seconds since the last, on screen, at a sane speed, with cinematics on. 'dim': the
        /// veil's darkness (0 none); 'title': a locale key for a title card (when title cards are on);
        /// 'slow'/'slowFor': the world at that rate for that long. False: it plays without one.
        /// While it plays, this move and its victims keep moving through freezes, and TopLayer draws
        /// above the veil.
        /// </summary>
        public bool BeginCinematic(float gap = 8f, float dim = 0.4f, string title = null, float slow = 0f, float slowFor = 0f)
        {
            if (_cine)
            {
                return true;
            }
            if (Settings.Cinematics <= 0 || !Cinema.FullAt(BodyPos()) || !Cinema.TryBegin(this, gap))
            {
                return false;
            }
            _cine = true;
            HideStar = true;
            if (dim > 0f)
            {
                Cinema.Dim(dim, Color.black, 0.15f, true);
            }
            if (!string.IsNullOrEmpty(title) && Settings.TitleCards)
            {
                Cinema.Title(Locale.Get(title), Hue.Edge, 1.6f, BodyPos());
            }
            if (slow > 0f && slowFor > 0f)
            {
                Cinema.SlowMo(slow, slowFor);
            }
            return true;
        }

        /// <summary>Freezes the world for 'seconds' (only while this move holds a cinematic).</summary>
        public void Freeze(float seconds)
        {
            if (_cine)
            {
                Cinema.Freeze(seconds);
            }
        }

        /// <summary>Ends this move's cinematic (the veil lifts).</summary>
        public void EndCinematic()
        {
            if (_cine)
            {
                _cine = false;
                Cinema.End(this);
            }
        }

        /// <summary>Opens a cinematic at this point of the script (see BeginCinematic).</summary>
        public ChoreoMove OpenCinematic(float gap = 8f, float dim = 0.4f, string title = null, float slow = 0f, float slowFor = 0f)
        {
            return Do(m => m.BeginCinematic(gap, dim, title, slow, slowFor));
        }
    }
}
