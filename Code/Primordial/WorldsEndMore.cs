using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Sakuga
{
    /// <summary>
    /// More of the world's end: eight more anime bouts and three more catastrophes, so that no two
    /// battles between Primordials look alike (each battle draws 14 of the 16 bouts and 7 of the 9
    /// catastrophes, in a new order).
    ///   BOUTS
    ///   Blade Storm       blades of light; they dash through each other a dozen times, every cut a
    ///                     crescent flying on across the land, cutting it; a last cross of light
    ///   World Charge      it rises and pulls the light out of everything it can see into one sphere,
    ///                     throws it; the other catches it, holds it, and it goes off in its arms
    ///   Frozen Instant    the moment stops: it is all round the other at once, eight of it, frozen
    ///                     mid-blow; the moment moves and every blow lands together
    ///   Crushing Gravity  the sky presses down: the other is driven into the ground, the ground sinks
    ///                     into a crater lake round it, and it bursts out
    ///   Mountain Hurl     a mountain torn out of the land (the hole fills with sea) and hurled; the other
    ///                     smashes it and the shards rain down on the land for miles
    ///   Twin Serpents     two serpents of light coil round each other in the sky, then dive and plough
    ///                     across the land at their masters' enemies
    ///   Echo Barrage      they blur into a ring of clashes, afterimages left hanging at every one,
    ///                     streaks of light joining them, and a last clash in the middle
    ///   Sky Tear          the sky is torn open over the other and the void falls out of it
    ///   CATASTROPHES
    ///   Tidal Apocalypse  the sea comes up over the land round the other, a wall of water drowning it
    ///   Absolute Zero     the cold before the first light: land to permafrost, water to ice, every
    ///                     living thing frozen and shattered, for a third of a continent
    ///   World Shatter     the world cracks: rivers of lava torn out from the other to the ends of the
    ///                     world, the ground sinking along them, the earth quaking
    /// </summary>
    internal sealed partial class WorldsEnd
    {
        // per-bout state of the new bouts
        private readonly List<Vector2> _tear = new List<Vector2>();
        private readonly Vector2[] _snakeA = new Vector2[22], _snakeB = new Vector2[22];
        private readonly List<Vector3> _ghosts = new List<Vector3>();
        private Vector2 _orb;
        private float _orbR, _flood;

        private float _skT, _skDash, _skAng;
        private bool _skHit;

        /// <summary>
        /// While a catastrophe plays out the fight doesn't stop: the one who threw it holds its ground
        /// and the other keeps coming at it, circling, darting in, every blow a crater, or firing on it.
        /// </summary>
        private void Skirmish(float dt)
        {
            Hold c = HAtk, o = HDef;
            if (c == null || o == null || c.Finished || o.Finished)
            {
                return;
            }
            c.Pose.Nudge = Vector2.zero;
            Hover(c, c == _ha ? PosA : PosB, Air, o.Pose.Ground);
            _skAng += dt * 2.4f;
            if (_skDash > 0f)
            {
                _skDash -= dt;
                Vector2 dir = o.Pose.Ground - c.Pose.Ground;
                dir = dir.sqrMagnitude > 0.01f ? dir.normalized : _axis;
                bool there = Go(o, c.Pose.Ground + dir * 0.8f * _k, c.Pose.Height, 80f, dt);
                if (there && !_skHit)
                {
                    _skHit = true;
                    Vector2 mid = (Body(c) + Body(o)) * 0.5f;
                    Impact(mid, (c.Pose.Ground + o.Pose.Ground) * 0.5f, 2.5f, 2, false);
                    PrimFx.Slash(mid, Random.Range(0f, 180f), 2.4f * _k, 0.28f * _k, 0.22f, FxLayer.Top, 24, false, Primordials.Prism(Random.value));
                    c.Pose.Nudge = new Vector2(Random.value < 0.5f ? -0.8f : 0.8f, 0f);
                    Strike(Defender, Attacker, 0.0015f);
                    _skDash = Mathf.Min(_skDash, 0.08f);
                }
            }
            else
            {
                Vector2 orbit = c.Pose.Ground + new Vector2(Mathf.Cos(_skAng), Mathf.Sin(_skAng) * 0.55f) * 5f * _k;
                Go(o, orbit, Air + Mathf.Sin(_t * 3f) * 0.6f * _k, 40f, dt);
                _skT -= dt;
                if (_skT <= 0f)
                {
                    _skT = Random.Range(0.25f, 0.5f);
                    if (Random.value < 0.3f)
                    {
                        PrimFx.Orb(Body(o), Body(c), 0.35f * _k, 0.16f, Primordials.Prism(Random.value, 0.7f), FxLayer.Top, 24, false);
                        Strike(Defender, Attacker, 0.001f);
                    }
                    else
                    {
                        _skDash = 0.4f;
                        _skHit = false;
                    }
                }
            }
            FaceTo(c, o.Pose.Ground);
            FaceTo(o, c.Pose.Ground);
        }

        private void ResetMore()
        {
            _tear.Clear();
            _ghosts.Clear();
            _orbR = 0f;
            _flood = 0f;
        }

        private static string MoreTitle(string p)
        {
            switch (p)
            {
                case "tide": return "TIDAL APOCALYPSE";
                case "frost": return "ABSOLUTE ZERO";
                case "shatter": return "WORLD SHATTER";
                default: return null;
            }
        }

        /// <summary>Its shape, held for a while where it is now (or at 'at'): an echo of it.</summary>
        private void Echo(Hold h, Vector2 ground, float height, float life, Color c0, Color c1)
        {
            if (h == null || !Vis(ground, 1f))
            {
                return;
            }
            Sprite s = Fx.UnitSprite(h.A);
            if (s == null)
            {
                return;
            }
            Particle p = Fx.Spawn(s, ground + new Vector2(0f, height), 1f, life, FxLayer.Top, 19, true);
            if (p == null)
            {
                return;
            }
            p.SX = Mathf.Abs(h.A.current_scale.x) * (h.Pose.FaceRight ? 1f : -1f);
            p.SY = h.A.current_scale.y;
            p.Color(c0, c0, c1);
        }

        // ================================================================== BLADE STORM ==

        private bool Blades(float dt)
        {
            Hold a = HAtk, d = HDef;
            if (_bp == 0)
            {
                bool m1 = Go(a, _center - _axis * 5f * _k, Air, 40f, dt), m2 = Go(d, _center + _axis * 5f * _k, Air, 40f, dt);
                FaceTo(a, d.Pose.Ground);
                FaceTo(d, a.Pose.Ground);
                if (m1 && m2 || _pt > 0.7f)
                {
                    _bp = 1;
                    _bt = 0.2f;
                }
                return false;
            }
            _bt += dt;
            if (_bp == 1)
            {
                if (_bt >= 0.17f && _hits < 12)
                {
                    _bt = 0f;
                    _hits++;
                    bool fromA = (_hits & 1) == 1;
                    Hold s = fromA ? a : d, t = fromA ? d : a;
                    Vector2 from = s.Pose.Ground, at = t.Pose.Ground;
                    Vector2 dir = (at - from).sqrMagnitude > 0.01f ? (at - from).normalized : _axis;
                    dir = Choreo.Rotate(dir, Random.Range(-35f, 35f));
                    // it dashes through: a streak where it went, and it is on the far side
                    Vector2 past = Inside(at + dir * 4f * _k);
                    PrimFx.Streak(Body(s), past + new Vector2(0f, s.Pose.Height), 0.55f * _k, 0.16f, FxLayer.Top, 20, false, Primordials.Prism(_hits * 0.1f));
                    Echo(s, from, s.Pose.Height, 0.3f, Fx.WithA(Primordials.Prism(_hits * 0.1f), 0.7f), Fx.WithA(Color.white, 0f));
                    s.Pose.Ground = past;
                    _was[s] = past;
                    FaceTo(s, t.Pose.Ground);
                    FaceTo(t, s.Pose.Ground);
                    // the cut: a blade of light through the other, and a crescent flying on, cutting the land
                    float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f + Random.Range(-25f, 25f);
                    PrimFx.Slash(Body(t), ang, 4.5f * _k, 0.5f * _k, 0.35f, FxLayer.Top, 24, false, Primordials.Prism(_hits * 0.13f));
                    Vector2 far = Inside(at + dir * 26f * WorldScale);
                    if (Vis(at, 1f))
                    {
                        // a crescent of light flying on across the land from the cut
                        float fly = (far - at).magnitude / 55f;
                        PrimFx.Slash(at, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 90f, 5f * _k, 0.8f * _k, Mathf.Max(0.2f, fly), FxLayer.Top, 22, false, Primordials.Prism(_hits * 0.13f + 0.3f), 0.03f, dir * 55f);
                    }
                    if (Settings.Destruction > 0)
                    {
                        Kit.Trench(at, far, 1.4f * WorldScale, 3, null, false);
                        ScarDisk(far, 6f * WorldScale, 0.1f);
                    }
                    DoomAround((at + far) * 0.5f, 3f);
                    Impact(Body(t), at, 3f, 2, false);
                    Strike(s.A, t.A, 0.004f);
                }
                if (_hits >= 12 && _bt > 0.25f)
                {
                    // the last cut: a cross of light on the defender, the ground under it split
                    Vector2 b = Body(d);
                    PrimFx.Slash(b, 45f, 9f * _k, 0.9f * _k, 0.6f, FxLayer.Top, 25, false, Color.white);
                    PrimFx.Slash(b, -45f, 9f * _k, 0.9f * _k, 0.6f, FxLayer.Top, 25, false, Primordials.Prism(), 0.08f);
                    Impact(b, d.Pose.Ground, 8f, 4, true);
                    Blast(d.Pose.Ground, Big(14), false, Attacker);
                    Hurt(0.05f);
                    _bp = 2;
                    _bt = 0f;
                }
                return false;
            }
            return _bt > 0.3f;
        }

        // ================================================================= WORLD CHARGE ==

        private bool WorldCharge(float dt)
        {
            Hold a = HAtk, d = HDef;
            switch (_bp)
            {
                case 0:
                    {
                        bool up = Go(a, a.Pose.Ground, Air + 5f * _k, 25f, dt);
                        Go(d, Inside(a.Pose.Ground + _axis * (Defender == _b ? 1f : -1f) * 16f * _k), Air, 30f, dt);
                        FaceTo(a, d.Pose.Ground);
                        FaceTo(d, a.Pose.Ground);
                        if (up || _pt > 0.8f)
                        {
                            _bp = 1;
                            _bt = 0f;
                        }
                        return false;
                    }
                case 1:
                    {
                        // the light of everything it can see, pulled into one sphere over its head
                        _bt += dt;
                        float k = Mathf.Clamp01(_bt / 2.2f);
                        _orbR = (0.5f + 4.5f * Anim.OutCubic(k)) * _k;
                        _orb = Body(a) + new Vector2(0f, 1.5f * _k + _orbR);
                        a.Pose.Rot = -10f;
                        PrimFx.Charge(_orb, _orbR, FxLayer.Top, 25, false, Primordials.Prism(_bt * 0.2f));
                        Camera cam = World.world.camera;
                        if (cam != null)
                        {
                            float hh = cam.orthographicSize, hw = hh * cam.aspect;
                            Vector2 vc = cam.transform.position;
                            for (int i = 0; i < 5; i++)
                            {
                                Vector2 from = vc + new Vector2(Random.Range(-hw, hw), Random.Range(-hh, hh));
                                float life = Random.Range(0.4f, 0.7f);
                                Particle p = Fx.Spawn(Art.Sparks[1], from, Mathf.Max(_px, hh / 110f), life, FxLayer.Top, 24);
                                if (p == null)
                                {
                                    break;
                                }
                                p.Moving((_orb - from) / life);
                                p.FaceVel = true;
                                Color c = Primordials.Prism(Random.value, 0.6f);
                                p.Color(Fx.WithA(c, 0f), Fx.WithA(c, 0.9f), Color.white);
                            }
                        }
                        if (Random.value < 0.3f && Settings.Destruction > 0)
                        {
                            Vector2 g = RandomLand(a.Pose.Ground, 14f * _k);
                            Spec.Rocks(g, _px, 3, 60f, _k);
                            Kit.Ravage(g, 1.5f, 2, null, false);
                        }
                        if (_bt >= 2.2f)
                        {
                            _bp = 2;
                            Choreo.Sound("event:/SFX/POWERS/Powerup", Combat.TileAt(Inside(a.Pose.Ground)));
                        }
                        return false;
                    }
                case 2:
                    {
                        // thrown
                        a.Pose.Rot = 18f;
                        Vector2 to = Body(d);
                        _orb = Vector2.MoveTowards(_orb, to, 26f * _k * dt);
                        PrimFx.Charge(_orb, _orbR, FxLayer.Top, 25, false, Primordials.Prism(_pt * 0.2f));
                        DuelFx.Comet(_orb - (to - _orb).normalized * _orbR * 2.2f, _orb, _px * 2f, Color.white, Primordials.Prism());
                        if (Settings.Destruction > 0 && Random.value < 0.4f)
                        {
                            Kit.Ravage(Inside(new Vector2(_orb.x, _orb.y - Air)), 2f, 2, null, false);
                        }
                        if ((_orb - to).sqrMagnitude < _orbR * _orbR)
                        {
                            _bp = 3;
                            _bt = 0f;
                        }
                        return false;
                    }
                case 3:
                    {
                        // caught: it holds it in its arms, shaking, the ground under it giving way
                        _bt += dt;
                        d.Pose.Nudge = new Vector2(((int)(_bt * 60f) & 1) == 0 ? 0.8f : -0.8f, 0f);
                        _orb = Body(d) + new Vector2(Mathf.Sin(_bt * 40f) * 0.1f, 0f) * _k;
                        PrimFx.Charge(_orb, _orbR * (1f + 0.15f * Mathf.Sin(_bt * 30f)), FxLayer.Top, 25, false, Primordials.Prism(_pt * 0.2f));
                        if (Settings.Destruction > 0 && Random.value < 0.5f)
                        {
                            Kit.Ravage(d.Pose.Ground + Random.insideUnitCircle * 4f * _k, 2.5f, 3, null, false);
                        }
                        if (_bt >= 1f)
                        {
                            d.Pose.Nudge = Vector2.zero;
                            Burst(_orb, 16f * _k, true);
                            Blast(d.Pose.Ground, Big(24), false, Attacker);
                            ScarDisk(d.Pose.Ground, Big(24) * 1.6f, 0.3f);
                            Hurt(0.07f);
                            Kit.Stop(_orb, 0.2f);
                            _bp = 4;
                            _bt = 0f;
                        }
                        return false;
                    }
                default:
                    _bt += dt;
                    Go(a, a.Pose.Ground, Air, 10f, dt);
                    Go(d, d.Pose.Ground - _axis * (Defender == _b ? -1f : 1f) * 3f * _k, Air, 12f, dt);
                    return _bt > 0.4f;
            }
        }

        // =============================================================== FROZEN INSTANT ==

        private bool FrozenInstant(float dt)
        {
            Hold a = HAtk, d = HDef;
            _bt += dt;
            if (_bp == 0)
            {
                // the moment stops
                Primordials.Flash(Fx.WithA(new Color(0.7f, 0.7f, 0.85f, 1f), 0.45f), 0.3f);
                if (Vis(Body(d), 1f))
                {
                    Fx.Ring(Body(d), _px, 1.6f, 40f * _k, 14f * _k, 2, 1, FxLayer.Top, 22, 1f, 2f, Fx.WithA(Color.white, 0.9f), Fx.WithA(new Color(0.7f, 0.75f, 1f, 1f), 0.6f), Fx.WithA(Color.white, 0f));
                }
                Choreo.Sound("event:/SFX/POWERS/Curse", Combat.TileAt(Inside(d.Pose.Ground)));
                _bp = 1;
                _bt = 0f;
                _hits = 0;
                return false;
            }
            if (_bp == 1)
            {
                // it is all round the other at once: eight of it, held mid-blow
                d.Pose.Nudge = Vector2.zero;
                if (_bt >= 0.13f && _hits < 8)
                {
                    _bt = 0f;
                    float ang = _hits * 45f + Random.Range(-10f, 10f);
                    Vector2 off = Choreo.Rotate(Vector2.right, ang) * 1.3f * _k;
                    a.Pose.Ground = Inside(d.Pose.Ground + new Vector2(off.x, off.y * 0.6f));
                    a.Pose.Height = d.Pose.Height + off.y * 0.4f;
                    _was[a] = a.Pose.Ground;
                    FaceTo(a, d.Pose.Ground);
                    a.Pose.Rot = 20f;
                    Echo(a, a.Pose.Ground, a.Pose.Height, 1.6f - _hits * 0.13f, Fx.WithA(new Color(0.85f, 0.85f, 1f, 1f), 0.85f), Fx.WithA(Primordials.Prism(_hits / 8f), 0f));
                    _ghosts.Add(new Vector3(off.x, off.y, ang));
                    // the blow, waiting in the air
                    PrimFx.Slash(Body(d) + off * 0.4f, ang + 90f, 2.6f * _k, 0.3f * _k, 1.6f - _hits * 0.13f, FxLayer.Top, 23, false, Fx.WithA(Primordials.Prism(_hits / 8f), 0.55f));
                    _hits++;
                }
                if (_hits >= 8 && _bt > 0.25f)
                {
                    // the moment moves: every blow at once
                    Vector2 b = Body(d);
                    Primordials.Flash(Fx.WithA(Color.white, 0.85f), 0.35f);
                    foreach (Vector3 g in _ghosts)
                    {
                        PrimFx.Slash(b, g.z + 90f, 6f * _k, 0.6f * _k, 0.4f, FxLayer.Top, 25, false, Primordials.Prism(g.z / 360f));
                    }
                    Impact(b, d.Pose.Ground, 9f, 4, true);
                    Blast(d.Pose.Ground, Big(18), false, Attacker);
                    ScarDisk(d.Pose.Ground, Big(18) * 1.5f, 0.2f);
                    Hurt(0.07f);
                    Kit.Stop(b, 0.25f);
                    _p0 = Landward(d.Pose.Ground, (d.Pose.Ground - a.Pose.Ground).sqrMagnitude > 0.01f ? (d.Pose.Ground - a.Pose.Ground).normalized : _axis, 26f);
                    _bp = 2;
                    _bt = 0f;
                }
                return false;
            }
            if (_bp == 2)
            {
                // flung away, ploughing the land
                bool there = Go(d, _p0, 0.5f * _k, 60f, dt, false);
                d.Pose.Rot += 1100f * dt;
                a.Pose.Rot = 0f;
                if (there)
                {
                    Impact(Body(d), _p0, 7f, 3, true);
                    d.Pose.Rot = 0f;
                    _bp = 3;
                    _bt = 0f;
                }
                return false;
            }
            Go(d, d.Pose.Ground, Air, 10f, dt);
            return _bt > 0.3f;
        }

        // ============================================================= CRUSHING GRAVITY ==

        private bool Gravity(float dt)
        {
            Hold a = HAtk, d = HDef;
            switch (_bp)
            {
                case 0:
                    {
                        _p0 = Land(d.Pose.Ground) ? d.Pose.Ground : LandNear(d.Pose.Ground, 40f);
                        bool m1 = Go(a, _p0, Air + 6f * _k, 40f, dt), m2 = Go(d, _p0, Air, 40f, dt);
                        if (m1 && m2 || _pt > 0.8f)
                        {
                            _bp = 1;
                            _bt = 0f;
                            _sinkDone = 0f;
                            Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(Inside(_p0)));
                        }
                        return false;
                    }
                case 1:
                    {
                        // the sky presses down on it
                        _bt += dt;
                        float k = Mathf.Clamp01(_bt / 1.8f);
                        a.Pose.Rot = -6f;
                        d.Pose.Height = Mathf.Lerp(Air, 0f, Anim.InQuad(k));
                        d.Pose.SX = 1f + 0.25f * k;
                        d.Pose.SY = 1f - 0.3f * k;
                        d.Pose.Nudge = new Vector2(((int)(_bt * 60f) & 1) == 0 ? 0.6f : -0.6f, 0f);
                        if (Random.value < 0.4f && Vis(_p0, 1f))
                        {
                            float r = Random.Range(10f, 16f) * _k;
                            Fx.Ring(_p0, _px, 0.35f, r / _px, 2f * _k / _px, 3, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Primordials.Void, 0f), Fx.WithA(new Color(0.6f, 0.45f, 1f, 1f), 0.8f), Fx.WithA(Color.white, 0.9f));
                            PrimFx.Beam(Body(a), Body(d), 0.5f * _k, 0.06f, FxLayer.Top, 20, false, new Color(0.45f, 0.3f, 0.9f, 1f), Primordials.Void, false, true);
                        }
                        // and the ground under it sinks, ring by ring, into a crater lake
                        float rr = 11f * WorldScale * k;
                        if (Settings.Destruction > 0 && rr > _sinkDone)
                        {
                            Vector2 c = _p0;
                            Annulus(c, _sinkDone, rr, t =>
                            {
                                if (t.hasBuilding() && t.building != null && t.building.isAlive())
                                {
                                    t.building.startDestroyBuilding();
                                }
                                MapAction.decreaseTile(t, true, TerraformLibrary.nothing);
                                float dx = t.x + 0.5f - c.x, dy = t.y + 0.5f - c.y;
                                if (dx * dx + dy * dy < rr * rr * 0.36f)
                                {
                                    MapAction.decreaseTile(t, true, TerraformLibrary.nothing);
                                }
                            });
                            _sinkDone = rr;
                        }
                        DoomAround(_p0, rr + 2f);
                        if (Random.value < 0.25f)
                        {
                            PrimFx.Crack(_p0, Random.insideUnitCircle.normalized, (6f + 10f * k) * _k, 2.5f * _px, 1f, new Color(0.7f, 0.5f, 1f, 1f), FxLayer.Ground, 4, false, true, 1, 0f, 6f);
                        }
                        if (_bt >= 1.8f)
                        {
                            // it bursts out
                            d.Pose.SX = d.Pose.SY = 1f;
                            d.Pose.Nudge = Vector2.zero;
                            Burst(Body(d), 10f * _k, true);
                            Blast(_p0, Big(12), false, Attacker);
                            ScarDisk(_p0, 11f * WorldScale * 1.6f, 0.15f);
                            Hurt(0.06f);
                            _bp = 2;
                            _bt = 0f;
                        }
                        return false;
                    }
                default:
                    _bt += dt;
                    Go(d, d.Pose.Ground, Air, 20f, dt);
                    Go(a, a.Pose.Ground + _axis * 2f * _k * dt, Air, 10f, dt);
                    return _bt > 0.3f;
            }
        }

        // ================================================================ MOUNTAIN HURL ==

        private bool MountainHurl(float dt)
        {
            Hold a = HAtk, d = HDef;
            switch (_bp)
            {
                case 0:
                    {
                        Vector2 away = a.Pose.Ground - d.Pose.Ground;
                        away = away.sqrMagnitude > 0.01f ? away.normalized : -_axis;
                        _p0 = Landward(a.Pose.Ground, away, 8f);
                        _bp = 1;
                        return false;
                    }
                case 1:
                    if (Go(a, _p0, 0.4f * _k, 45f, dt) || _pt > 0.8f)
                    {
                        // it tears a mountain out of the land: the hole it leaves fills with the sea
                        float r = 5f * WorldScale;
                        if (Settings.Destruction > 0)
                        {
                            Annulus(_p0, 0f, r, t =>
                            {
                                if (t.hasBuilding() && t.building != null && t.building.isAlive())
                                {
                                    t.building.startDestroyBuilding();
                                }
                                MapAction.decreaseTile(t, true, TerraformLibrary.nothing);
                                MapAction.decreaseTile(t, true, TerraformLibrary.nothing);
                            });
                        }
                        DoomAround(_p0, r + 1f);
                        Spec.Rocks(_p0, _px, 30, 120f, _k);
                        Kit.Shake(_p0, 0.5f, 0.015f);
                        Choreo.Sound("event:/SFX/DESTRUCTION/DropSimpleImpact", Combat.TileAt(Inside(_p0)));
                        _orb = _p0;
                        _orbR = 3.5f * _k * Mathf.Sqrt(WorldScale);
                        _bp = 2;
                        _bt = 0f;
                    }
                    return false;
                case 2:
                    {
                        // lifted over its head
                        _bt += dt;
                        _orb = Vector2.Lerp(_orb, Body(a) + new Vector2(0f, 2f * _k + _orbR), 0.12f);
                        _rockRot = Mathf.Sin(_bt * 7f) * 6f;
                        a.Pose.Height = Mathf.Lerp(a.Pose.Height, Air, 0.08f);
                        Rock(_orb, _orbR);
                        FaceTo(a, d.Pose.Ground);
                        if (_bt >= 1.1f)
                        {
                            _bp = 3;
                        }
                        return false;
                    }
                case 3:
                    {
                        // hurled
                        a.Pose.Rot = 20f;
                        Vector2 to = Body(d);
                        _orb = Vector2.MoveTowards(_orb, to, 30f * _k * dt);
                        _rockRot += 160f * dt;
                        Rock(_orb, _orbR);
                        if (Random.value < 0.5f)
                        {
                            Spec.Rocks(_orb, _px, 2, 30f, _k);
                        }
                        if ((_orb - to).sqrMagnitude < _orbR * _orbR)
                        {
                            // smashed with one blow: the shards rain down far and wide
                            Burst(_orb, 10f * _k, false);
                            Impact(_orb, d.Pose.Ground, 4f, 2, true);
                            Hurt(0.03f);
                            _falls.Clear();
                            int n = Mathf.RoundToInt(16 * WorldScale);
                            for (int i = 0; i < n; i++)
                            {
                                Vector2 at = RandomLand(d.Pose.Ground, 22f * WorldScale);
                                _falls.Add(new Vector3(at.x, at.y, _pt + Random.Range(0.4f, 1.3f)));
                                if (Vis(_orb, 2f) || Vis(at, 1f))
                                {
                                    Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], _orb, _px * Random.Range(3f, 5f), _falls[i].z - _pt, FxLayer.Top, 22);
                                    if (c != null)
                                    {
                                        c.Moving((at - _orb) / Mathf.Max(0.1f, _falls[i].z - _pt));
                                        c.Spin = Random.Range(-400f, 400f);
                                        c.Color(new Color(0.55f, 0.5f, 0.48f, 1f));
                                    }
                                }
                            }
                            _bp = 4;
                            _bt = 0f;
                        }
                        return false;
                    }
                default:
                    {
                        for (int i = _falls.Count - 1; i >= 0; i--)
                        {
                            if (_pt >= _falls[i].z)
                            {
                                Vector2 at = new Vector2(_falls[i].x, _falls[i].y);
                                _falls.RemoveAt(i);
                                Impact(at + new Vector2(0f, 0.5f), at, 4f, 3, false);
                                if (Settings.Destruction > 0)
                                {
                                    MapAction.damageWorld(Combat.TileAt(at), 5, TerraformLibrary.bomb, null);
                                }
                                ScarDisk(at, 7f, 0.15f);
                            }
                        }
                        Go(d, d.Pose.Ground, Air, 10f, dt);
                        Go(a, a.Pose.Ground, Air, 10f, dt);
                        a.Pose.Rot = 0f;
                        return _falls.Count == 0 && _pt > 1f;
                    }
            }
        }

        private float _rockRot;
        private static Sprite[] _chunk;

        /// <summary>
        /// A chunk of the land torn out whole (pixel art, made once): grass on its surface, a band of soil,
        /// shaded stone under it with cracks and a dark outline, and rock roots hanging where it was torn out.
        /// Three sprites (stone, soil, grass) so each takes its own colour.
        /// </summary>
        private static Sprite[] Chunk()
        {
            if (_chunk != null)
            {
                return _chunk;
            }
            const int W = 52, H = 46;
            Canvas stone = new Canvas(W, H), soil = new Canvas(W, H), grass = new Canvas(W, H);
            System.Random rnd = new System.Random(11);
            float[] top = new float[W], bot = new float[W];
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f;
                float au = Mathf.Abs(u);
                top[x] = H - 5f - 1.2f * Mathf.Sin(x * 0.45f) - (au > 0.8f ? (au - 0.8f) * 40f : 0f);
                float depth = (H - 10f) * Mathf.Pow(Mathf.Max(0f, 1f - au), 0.9f) * (0.75f + 0.25f * (float)rnd.NextDouble());
                if (x % 6 == 3)
                {
                    depth += 4f * (1f - au);   // a rock root hanging lower
                }
                bot[x] = top[x] - 6f - depth;
            }
            bool In(int x, int y) => x >= 0 && x < W && y >= 0 && y < H && y <= top[x] && y >= bot[x];
            // a few cracks in the stone, running up from the torn underside
            HashSet<int> cracks = new HashSet<int>();
            for (int c = 0; c < 5; c++)
            {
                int cx = rnd.Next(8, W - 8), cy = (int)bot[cx] + 1;
                for (int k = 0; k < 14 && In(cx, cy); k++)
                {
                    cracks.Add(cy * W + cx);
                    cy++;
                    cx += rnd.Next(-1, 2);
                }
            }
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    if (!In(x, y))
                    {
                        continue;
                    }
                    bool edge = !In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1);
                    float below = top[x] - y;
                    // light from the upper left, in four flat steps (pixel art, not a gradient)
                    float lit = 1f - 0.45f * (x / (float)W) - 0.4f * (below / H);
                    float v = Mathf.Round(Mathf.Clamp01(lit) * 4f) / 4f * 0.55f + 0.4f;
                    if (edge)
                    {
                        v = 0.22f;
                    }
                    if (below < 2.5f)
                    {
                        grass.Set(x, y, edge ? 0.45f : (x + y) % 3 == 0 ? 0.8f : 1f, 1f);
                    }
                    else if (below < 7f)
                    {
                        soil.Set(x, y, edge ? 0.35f : v * ((x * 7 + y * 3) % 5 == 0 ? 0.8f : 1f), 1f);
                    }
                    else
                    {
                        if (cracks.Contains(y * W + x))
                        {
                            v = 0.25f;
                        }
                        else if ((x * 13 + y * 7) % 11 == 0)
                        {
                            v = Mathf.Min(1f, v + 0.15f);   // a glint of a facet
                        }
                        stone.Set(x, y, v, 1f);
                    }
                }
            }
            // blades of grass standing up off the surface
            for (int x = 1; x < W - 1; x += 2)
            {
                int y = Mathf.RoundToInt(top[x]) + 1;
                if (y < H && rnd.NextDouble() < 0.6)
                {
                    grass.Set(x, y, 0.85f, 1f);
                }
            }
            _chunk = new[]
            {
                stone.ToSprite("sakuga_chunk_stone", new Vector2(0.5f, 0.5f)),
                soil.ToSprite("sakuga_chunk_soil", new Vector2(0.5f, 0.5f)),
                grass.ToSprite("sakuga_chunk_grass", new Vector2(0.5f, 0.5f))
            };
            return _chunk;
        }

        /// <summary>The torn-out land in the air, turning as it goes; stones and clods dropping off it, a trail of dust.</summary>
        private void Rock(Vector2 at, float r)
        {
            if (!Vis(at, 2f))
            {
                return;
            }
            Sprite[] s = Chunk();
            float cpx = 2.2f * r / 52f;
            Color[] tint = { new Color(0.66f, 0.63f, 0.6f, 1f), new Color(0.56f, 0.4f, 0.27f, 1f), new Color(0.42f, 0.7f, 0.32f, 1f) };
            for (int i = 0; i < 3; i++)
            {
                Particle p = Fx.Spawn(s[i], at, cpx, 0.05f, FxLayer.Top, 22 + i);
                if (p == null)
                {
                    break;
                }
                p.Rot = _rockRot;
                p.Color(tint[i]);
            }
            if (Random.value < 0.6f)
            {
                Vector2 o = Random.insideUnitCircle * r * 0.8f + new Vector2(0f, -r * 0.4f);
                Particle c = Fx.Spawn(Art.Chunks[Random.Range(0, Art.Chunks.Length)], at + o, _px * Random.Range(1.5f, 2.6f), 0.9f, FxLayer.Top, 21);
                c?.Moving(new Vector2(Random.Range(-2f, 2f), -Random.Range(4f, 9f)) * _k, 0f, 0f, 9f * _k).Color(Random.value < 0.5f ? tint[0] : tint[1], Fx.WithA(tint[1], 0f));
            }
            if (Random.value < 0.4f)
            {
                Particle d = Fx.Spawn(Art.Dust[Random.Range(0, 3)], at + Random.insideUnitCircle * r, _px * 2f, 0.6f, FxLayer.Top, 20);
                d?.Moving(Random.insideUnitCircle * 2f * _k, 0f, 1.5f).Color(Fx.WithA(tint[1], 0.6f), Fx.WithA(tint[1], 0f));
            }
        }

        // ================================================================ TWIN SERPENTS ==

        private bool Serpents(float dt)
        {
            if (_bp == 0)
            {
                for (int i = 0; i < _snakeA.Length; i++)
                {
                    _snakeA[i] = Body(_ha);
                    _snakeB[i] = Body(_hb);
                }
                _bp = 1;
                HoverBoth(Air);
                Choreo.Sound("event:/SFX/POWERS/DivineLight", Combat.TileAt(Inside(_center)));
                return false;
            }
            HoverBoth(Air);
            float t = _pt;
            Vector2 headA, headB;
            if (t < 1.6f)
            {
                // they coil round each other in the sky
                float w = 5f, R = 7f * _k, hgt = 7f * _k;
                headA = _center + new Vector2(Mathf.Cos(t * w), Mathf.Sin(t * w) * 0.5f) * R + new Vector2(0f, hgt + Mathf.Sin(t * 3f) * 2f * _k);
                headB = _center + new Vector2(Mathf.Cos(t * w + Mathf.PI), Mathf.Sin(t * w + Mathf.PI) * 0.5f) * R + new Vector2(0f, hgt + Mathf.Cos(t * 3f) * 2f * _k);
                _p0 = _hb.Pose.Ground;
                _p1 = _ha.Pose.Ground;
            }
            else
            {
                // and dive, ploughing across the land through their masters' enemies
                float k = Mathf.Clamp01((t - 1.6f) / 1.1f);
                Vector2 da = (_p0 - _center).sqrMagnitude > 0.01f ? (_p0 - _center).normalized : _axis;
                Vector2 db = (_p1 - _center).sqrMagnitude > 0.01f ? (_p1 - _center).normalized : -_axis;
                Vector2 ta = Inside(_p0 + da * 26f * WorldScale * k), tb = Inside(_p1 + db * 26f * WorldScale * k);
                headA = Vector2.Lerp(_snakeA[0], ta, 0.35f);
                headB = Vector2.Lerp(_snakeB[0], tb, 0.35f);
                if (Settings.Destruction > 0)
                {
                    Kit.Trench(_snakeA[0], headA, 2.2f * WorldScale, 3, null, false);
                    Kit.Trench(_snakeB[0], headB, 2.2f * WorldScale, 3, null, false);
                }
                DoomAround(headA, 3f);
                DoomAround(headB, 3f);
                if (Random.value < 0.2f)
                {
                    ScarDisk(headA, 6f, 0.2f);
                    ScarDisk(headB, 6f, 0.2f);
                }
                if (Random.value < 0.25f)
                {
                    Impact(headA, headA, 3f, 2, false);
                    Impact(headB, headB, 3f, 2, false);
                }
            }
            for (int i = _snakeA.Length - 1; i > 0; i--)
            {
                _snakeA[i] = Vector2.Lerp(_snakeA[i], _snakeA[i - 1], 0.55f);
                _snakeB[i] = Vector2.Lerp(_snakeB[i], _snakeB[i - 1], 0.55f);
            }
            _snakeA[0] = headA;
            _snakeB[0] = headB;
            Serpent(_snakeA, Primordials.Prism(0f));
            Serpent(_snakeB, Primordials.Prism(0.5f));
            if (t >= 2.7f)
            {
                Impact(headA, headA, 8f, 4, true);
                Impact(headB, headB, 8f, 4, true);
                Strike(_a, _b, 0.025f);
                Strike(_b, _a, 0.025f);
                return true;
            }
            return false;
        }

        /// <summary>A serpent of light: a glowing body of scales thinning to the tail, a crested head, eyes.</summary>
        private void Serpent(Vector2[] s, Color c)
        {
            if (!Vis(s[0], 2f) && !Vis(s[s.Length / 2], 2f))
            {
                return;
            }
            for (int i = s.Length - 1; i >= 0; i--)
            {
                float k = 1f - i / (float)s.Length;
                float r = (0.25f + 0.75f * k) * 0.9f * _k;
                Spec.Blob(s[i], r * 1.5f, _px, 0.05f, FxLayer.Top, 20, 1f, 0.6f, Fx.WithA(c, 0.35f));
                Spec.Blob(s[i], r, _px, 0.05f, FxLayer.Top, 21, 1f, 0.2f, Color.Lerp(c, Color.white, 0.25f + 0.3f * (i & 1)));
                if (i % 3 == 0)
                {
                    Spec.Blob(s[i] + new Vector2(0f, r * 0.4f), r * 0.35f, _px, 0.05f, FxLayer.Top, 22, 1f, 0.2f, Color.white);
                }
            }
            Vector2 head = s[0], dir = (s[0] - s[1]).sqrMagnitude > 1e-4f ? (s[0] - s[1]).normalized : Vector2.right;
            Vector2 side = new Vector2(-dir.y, dir.x);
            Spec.Blob(head, 1.3f * _k, _px, 0.05f, FxLayer.Top, 22, 1f, 0.2f, Color.Lerp(c, Color.white, 0.4f));
            PrimFx.Slash(head - dir * 0.6f * _k + side * 0.7f * _k, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg + 150f, 1.6f * _k, 0.3f * _k, 0.05f, FxLayer.Top, 23, false, c);
            PrimFx.Slash(head - dir * 0.6f * _k - side * 0.7f * _k, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 150f, 1.6f * _k, 0.3f * _k, 0.05f, FxLayer.Top, 23, false, c);
            Spec.Blob(head + dir * 0.5f * _k + side * 0.4f * _k, 0.22f * _k, _px, 0.05f, FxLayer.Top, 24, 1f, 0f, Color.white);
            Spec.Blob(head + dir * 0.5f * _k - side * 0.4f * _k, 0.22f * _k, _px, 0.05f, FxLayer.Top, 24, 1f, 0f, Color.white);
            if (Random.value < 0.5f)
            {
                Kit.Ember(s[Random.Range(0, s.Length)], _px, 0.5f, new Vector2(Random.Range(-5f, 5f), Random.Range(5f, 15f)) * _px, c);
            }
        }

        // ================================================================= ECHO BARRAGE ==

        private bool EchoBarrage(float dt)
        {
            Hold a = _ha, b = _hb;
            _bt += dt;
            if (_bp == 0)
            {
                foreach (Hold h in new[] { a, b })
                {
                    if (Vis(Body(h), 1f))
                    {
                        DuelFx.Blink(h.Pose.Ground, _px, _k, Color.white, Primordials.Prism());
                    }
                    h.Pose.Hidden = true;
                }
                _p2 = RandomLand(_center, 4f);
                _p1 = _p2 + Vector2.right * 12f * _k;
                _bp = 1;
                _bt = 0f;
                return false;
            }
            const int n = 16;
            if (_bp == 1)
            {
                if (_bt >= 0.1f && _hits < n)
                {
                    _bt = 0f;
                    _hits++;
                    // round the ring of clashes, afterimages left hanging at each, streaks joining them
                    float ang = _hits * (360f / n) * 1.5f;
                    Vector2 at = Inside(_p2 + Choreo.Rotate(Vector2.right, ang) * new Vector2(12f, 7f).magnitude * _k * 0.8f);
                    Vector2 dir = Choreo.Rotate(Vector2.right, ang + 90f);
                    a.Pose.Ground = Inside(at - dir * 0.6f * _k);
                    b.Pose.Ground = Inside(at + dir * 0.6f * _k);
                    a.Pose.Height = b.Pose.Height = Random.Range(1f, 4f) * _k;
                    a.Pose.Hidden = b.Pose.Hidden = false;
                    FaceTo(a, b.Pose.Ground);
                    FaceTo(b, a.Pose.Ground);
                    _was[a] = a.Pose.Ground;
                    _was[b] = b.Pose.Ground;
                    Echo(a, a.Pose.Ground, a.Pose.Height, 0.9f, Fx.WithA(Primordials.Prism(_hits / (float)n), 0.75f), Fx.WithA(Color.white, 0f));
                    Echo(b, b.Pose.Ground, b.Pose.Height, 0.9f, Fx.WithA(Primordials.Prism(_hits / (float)n + 0.5f), 0.75f), Fx.WithA(Color.white, 0f));
                    PrimFx.Streak(_p1 + new Vector2(0f, 2f * _k), at + new Vector2(0f, 2f * _k), 0.45f * _k, 0.3f, FxLayer.Top, 19, false, Primordials.Prism(_hits / (float)n));
                    _p1 = at;
                    Impact((Body(a) + Body(b)) * 0.5f, at, 4f, 3, _hits % 3 == 0);
                    Strike(_a, _b, 0.003f);
                    Strike(_b, _a, 0.003f);
                }
                else if (_bt > 0.05f && _hits < n)
                {
                    a.Pose.Hidden = b.Pose.Hidden = true;
                }
                if (_hits >= n && _bt > 0.15f)
                {
                    // the last one, in the middle of the ring
                    a.Pose.Hidden = b.Pose.Hidden = false;
                    a.Pose.Ground = Inside(_p2 - _axis * 0.6f * _k);
                    b.Pose.Ground = Inside(_p2 + _axis * 0.6f * _k);
                    _was[a] = a.Pose.Ground;
                    _was[b] = b.Pose.Ground;
                    Impact((Body(a) + Body(b)) * 0.5f, _p2, 10f, 4, true);
                    Blast(_p2, Big(16), false, null);
                    Strike(_a, _b, 0.015f);
                    Strike(_b, _a, 0.015f);
                    _center = _p2;
                    _bp = 2;
                    _bt = 0f;
                }
                return false;
            }
            HoverBoth(Air);
            return _bt > 0.35f;
        }

        // ===================================================================== SKY TEAR ==

        private bool SkyTear(float dt)
        {
            Hold a = HAtk, d = HDef;
            _bt += dt;
            if (_bp == 0)
            {
                Go(a, a.Pose.Ground, Air + 3f * _k, 20f, dt);
                a.Pose.Rot = -15f;
                FaceTo(a, d.Pose.Ground);
                if (_bt > 0.5f)
                {
                    // the sky over the other, torn open
                    _tear.Clear();
                    Vector2 c = d.Pose.Ground + new Vector2(0f, 12f * _k);
                    Vector2 dir = Choreo.Rotate(Vector2.right, Random.Range(-20f, 20f));
                    float len = 18f * _k * Mathf.Sqrt(WorldScale);
                    for (int i = 0; i <= 8; i++)
                    {
                        _tear.Add(c + dir * len * (i / 8f - 0.5f) + new Vector2(0f, Random.Range(-1f, 1f) * _k * (i == 0 || i == 8 ? 0f : 1f)));
                    }
                    PrimFx.Slash(Body(a), -40f, 7f * _k, 0.7f * _k, 0.4f, FxLayer.Top, 24, false, Color.white);
                    Primordials.Flash(Fx.WithA(Primordials.Void, 0.5f), 0.3f);
                    Choreo.Sound("event:/SFX/POWERS/Curse", Combat.TileAt(Inside(d.Pose.Ground)));
                    _bp = 1;
                    _bt = 0f;
                    _falls.Clear();
                }
                return false;
            }
            HoverBoth(Air);
            float open = _bp == 1 ? Mathf.Clamp01(_bt / 0.4f) : 1f - Mathf.Clamp01(_bt / 0.4f);
            DrawTear(open);
            if (_bp == 1)
            {
                // the void falls out of it
                if (Random.value < 0.6f && _bt < 2.4f)
                {
                    Vector2 from = _tear[Random.Range(1, _tear.Count - 1)];
                    Vector2 to = RandomLand(d.Pose.Ground, 18f * WorldScale);
                    float fall = Random.Range(0.35f, 0.5f);
                    Color edge = Primordials.Prism(Random.value, 0.7f);
                    PrimFx.Meteor(from, to, Random.Range(0.45f, 0.75f) * _k, fall, edge, new Color(0.3f, 0.18f, 0.45f, 1f), Color.Lerp(edge, Primordials.Void, 0.4f), Primordials.Void, FxLayer.Top, 23, false);
                    _falls.Add(new Vector3(to.x, to.y, _pt + fall));
                }
                for (int i = _falls.Count - 1; i >= 0; i--)
                {
                    if (_pt >= _falls[i].z)
                    {
                        Vector2 at = new Vector2(_falls[i].x, _falls[i].y);
                        _falls.RemoveAt(i);
                        Impact(at + new Vector2(0f, 0.4f), at, 4f, 3, false);
                        if (Settings.Destruction > 0 && Random.value < 0.4f)
                        {
                            MapAction.damageWorld(Combat.TileAt(at), 4, TerraformLibrary.bomb, null);
                        }
                        ScarDisk(at, 6f, 0.15f);
                        if ((Defender.current_position - at).sqrMagnitude < 9f)
                        {
                            Strike(Attacker, Defender, 0.006f);
                        }
                    }
                }
                if (_bt >= 2.5f && _falls.Count == 0)
                {
                    _bp = 2;
                    _bt = 0f;
                }
                return false;
            }
            a.Pose.Rot = 0f;
            return _bt > 0.45f;
        }

        private void DrawTear(float open)
        {
            if (_tear.Count < 2 || open <= 0f)
            {
                return;
            }
            float w = 2.4f * _k * open;
            for (int i = 1; i < _tear.Count; i++)
            {
                float taper = Mathf.Sin(Mathf.PI * (i - 0.5f) / (_tear.Count - 1));
                Vector2 p0 = _tear[i - 1], p1 = _tear[i];
                PrimFx.Band(p0, p1, w * 1.8f * taper, 0.05f, FxLayer.Top, 20, false, 0, Fx.WithA(Primordials.Prism(i * 0.1f), 0.6f));
                PrimFx.Band(p0, p1, w * taper, 0.05f, FxLayer.Top, 21, false, 1, Primordials.Void);
            }
            if (Random.value < 0.3f)
            {
                Vector2 p = _tear[Random.Range(1, _tear.Count - 1)];
                PrimFx.Bolt(p, p + Random.insideUnitCircle * 4f * _k, 0.15f * _k, 0.08f, Primordials.Prism(Random.value), FxLayer.Top, 22, false, 1);
            }
        }

        // ============================================================= TIDAL APOCALYPSE ==

        private bool Tide(float dt)
        {
            float R = 55f * WorldScale;
            if (_bp == 0)
            {
                _p0 = WaterNear(Defender.current_position, 90f * WorldScale);
                _bp = 1;
                _bt = 0f;
                _flood = 0f;
                HAtk.Pose.Rot = -12f;
            }
            _bt += dt;
            if (_bt < 1f)
            {
                // the sea draws back and rises
                if (Vis(_p0, 2f) && Random.value < 0.6f)
                {
                    Spec.Blob(_p0 + Random.insideUnitCircle * 6f * _k, Random.Range(1f, 2.5f) * _k, _px, 0.4f, FxLayer.Top, 14, 1f, 0.5f, Fx.WithA(new Color(0.6f, 0.85f, 1f, 1f), 0.8f), Fx.WithA(new Color(0.2f, 0.45f, 0.9f, 1f), 0f))?.Moving(new Vector2(0f, 6f * _k));
                }
                World.world.startShake(0.2f, 0.01f, 1.5f);
                return false;
            }
            if (_flood <= 0f)
            {
                Burst(Body(HDef), 6f * _k, false);
                Wound(0.14f);
                Choreo.Sound("event:/SFX/POWERS/WaterBomb", Combat.TileAt(Inside(_p0)));
            }
            float r = Mathf.Min(R, (_bt - 1f) * 20f * WorldScale);
            if (Settings.Destruction > 0 && r > _flood)
            {
                Vector2 c = _p0;
                float rr = R;
                Annulus(c, _flood, r, t =>
                {
                    if (t.Type == null || t.Type.liquid)
                    {
                        return;
                    }
                    if (t.hasBuilding() && t.building != null && t.building.isAlive())
                    {
                        t.building.startDestroyBuilding();
                    }
                    if (!t.isTileRank(TileRank.Low) && !t.isTileRank(TileRank.High))
                    {
                        return;   // (the peaks stand out of it)
                    }
                    float dx = t.x + 0.5f - c.x, dy = t.y + 0.5f - c.y;
                    float far = Mathf.Sqrt(dx * dx + dy * dy) / rr;
                    MapAction.terraformMain(t, far < 0.55f ? TileLibrary.close_ocean : TileLibrary.shallow_waters, TerraformLibrary.nothing);
                });
            }
            DoomAround(_p0, r);
            _flood = Mathf.Max(_flood, r);
            // the wall of water where it can be seen
            for (int i = 0; i < 6; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                Vector2 at = _p0 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (!Vis(at, 1f))
                {
                    continue;
                }
                Spec.Blob(at, Random.Range(1f, 2.2f) * _k, _px, 0.5f, FxLayer.Top, 14, 1f, 0.45f, Color.white, Fx.WithA(new Color(0.55f, 0.8f, 1f, 1f), 0.85f), Fx.WithA(new Color(0.15f, 0.4f, 0.85f, 1f), 0f))
                    ?.Moving(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 6f * _k + new Vector2(0f, 3f * _k));
                Particle f = Fx.Spawn(Art.Puffs[0], at, _px * 1.5f, 0.4f, FxLayer.Top, 15);
                f?.Moving(new Vector2(0f, 8f * _k), 0f, 1f).Color(Color.white, Fx.WithA(new Color(0.7f, 0.9f, 1f, 1f), 0f));
            }
            return r >= R && _bt > 3f;
        }

        /// <summary>The sea nearest 'p' (rings outward), or 'p' itself: the water comes up out of the ground.</summary>
        private static Vector2 WaterNear(Vector2 p, float r)
        {
            for (float rr = 6f; rr <= r; rr += 6f)
            {
                float off = Random.value * 6.283f;
                for (int i = 0; i < 20; i++)
                {
                    float a = off + i * 0.314f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                    if (!OnMap(q))
                    {
                        continue;
                    }
                    WorldTile t = Combat.TileAt(q);
                    if (t != null && t.Type != null && t.Type.liquid && !t.Type.lava)
                    {
                        return q;
                    }
                }
            }
            return p;
        }

        // ================================================================ ABSOLUTE ZERO ==

        private bool Frost(float dt)
        {
            float R = 60f * WorldScale;
            Vector2 c = Defender.current_position;
            if (_bp == 0)
            {
                _p0 = c;
                _bp = 1;
                _bt = 0f;
                _flood = 0f;
            }
            _bt += dt;
            HAtk.Pose.Rot = -10f;
            if (_bt < 0.9f)
            {
                // the cold before the first light gathers in its hands
                PrimFx.Charge(Body(HAtk) + new Vector2(0f, 1.2f * _k), (0.4f + 2f * _bt) * _k, FxLayer.Top, 25, false, new Color(0.65f, 0.9f, 1f, 1f));
                return false;
            }
            if (_flood <= 0f)
            {
                Primordials.Flash(Fx.WithA(new Color(0.75f, 0.92f, 1f, 1f), 0.75f), 0.5f);
                Burst(Body(HDef), 6f * _k, false);
                Wound(0.14f);
                Choreo.Sound("event:/SFX/EXPLOSIONS/ExplosionForce", Combat.TileAt(Inside(_p0)));
            }
            float r = Mathf.Min(R, (_bt - 0.9f) * 24f * WorldScale);
            if (r > _flood)
            {
                if (Settings.Destruction > 0)
                {
                    Annulus(_p0, _flood, r, t =>
                    {
                        if (t.Type == null)
                        {
                            return;
                        }
                        if (t.hasBuilding() && t.building != null && t.building.isAlive())
                        {
                            t.building.startDestroyBuilding();
                        }
                        if (t.Type.lava)
                        {
                            return;
                        }
                        if (t.Type.liquid)
                        {
                            t.freeze(5);
                        }
                        else if (t.isTileRank(TileRank.Low))
                        {
                            MapAction.terraformTop(t, TopTileLibrary.permafrost_low, TerraformLibrary.nothing);
                        }
                        else if (t.isTileRank(TileRank.High))
                        {
                            MapAction.terraformTop(t, TopTileLibrary.permafrost_high, TerraformLibrary.nothing);
                        }
                    });
                }
                // every living thing frozen where it stands, and shattered
                Primordials.Around(null, _p0, r, true, _scan);
                foreach (Actor u in _scan)
                {
                    long id = u.Sid();
                    if (id == _idA || id == _idB)
                    {
                        continue;
                    }
                    try
                    {
                        u.addStatusEffect("frozen", 3f);
                    }
                    catch
                    {
                    }
                    if (Vis(u.current_position, 0.5f) && Random.value < 0.3f)
                    {
                        PrimFx.Slash(Choreo.BodyAt(u, 0.5f), Random.Range(0f, 180f), 1.2f * _k, 0.25f * _k, 0.4f, FxLayer.Top, 22, false, new Color(0.7f, 0.95f, 1f, 1f));
                    }
                }
                DoomAround(_p0, Mathf.Max(0f, r - 6f));
                _flood = r;
            }
            // the frost front where it can be seen: crystals shooting out of the ground, snow in the air
            for (int i = 0; i < 6; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                Vector2 at = _p0 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * Art.Ground) * r;
                if (!Vis(at, 1f))
                {
                    continue;
                }
                PrimFx.Slash(at + new Vector2(0f, 0.6f * _k), 90f + Random.Range(-30f, 30f), Random.Range(1.2f, 2.6f) * _k, 0.35f * _k, 1.2f, FxLayer.Top, 16, false, new Color(0.75f, 0.95f, 1f, 1f));
                Particle s = Fx.Spawn(Art.Disk(1, 1f, 0f), at + new Vector2(Random.Range(-6f, 6f), Random.Range(4f, 12f)) * _k, _px, 1.5f, FxLayer.Top, 17);
                s?.Moving(new Vector2(Random.Range(-1f, 1f), -Random.Range(2f, 4f)) * _k).Color(Color.white, Fx.WithA(new Color(0.8f, 0.95f, 1f, 1f), 0f));
            }
            if (Random.value < 0.4f && Vis(_p0, 3f))
            {
                Fx.Ring(_p0, _px, 0.4f, Mathf.Min(140f, r / _px * 0.9f), Mathf.Min(140f, r / _px), 2, 1, FxLayer.Ground, 6, Art.Ground, 2f, Fx.WithA(Color.white, 0.8f), Fx.WithA(new Color(0.6f, 0.9f, 1f, 1f), 0f));
            }
            return r >= R && _bt > 3.2f;
        }

        // ================================================================ WORLD SHATTER ==

        private bool Shatter(float dt)
        {
            Vector2 c = Defender.current_position;
            if (_bp == 0)
            {
                _p0 = c;
                _rays.Clear();
                int n = Mathf.RoundToInt(Random.Range(11, 16) * Mathf.Sqrt(WorldScale));
                for (int i = 0; i < n; i++)
                {
                    _rays.Add(Choreo.Rotate(Vector2.right, i * 360f / n + Random.Range(-10f, 10f)));
                }
                _bp = 1;
                _bt = 0f;
                _cutDone = 0f;
                HAtk.Pose.Rot = 25f;
                Burst(c, 7f * _k, true);
                Impact(c + new Vector2(0f, 1f), c, 9f, 4, true);
                Wound(0.14f);
                if (Settings.Destruction > 0)
                {
                    Earthquake.startQuake(Combat.TileAt(Inside(c)), EarthquakeType.BigDecrease);
                }
                Choreo.Sound("event:/SFX/NATURE/EarthQuake", Combat.TileAt(Inside(c)));
            }
            _bt += dt;
            float far = Mathf.Sqrt(MapBox.width * MapBox.width + MapBox.height * MapBox.height);
            float reach = Mathf.Min(far, _bt * 110f * WorldScale);
            while (_cutDone < reach)
            {
                float s0 = _cutDone;
                _cutDone += 5f;
                for (int i = 0; i < _rays.Count; i++)
                {
                    // each crack wanders a little as it goes
                    Vector2 d = Choreo.Rotate(_rays[i], Mathf.Sin(s0 * 0.04f + i * 1.7f) * 12f);
                    Vector2 a = _p0 + d * s0, b = _p0 + d * _cutDone;
                    if (!OnMap(a))
                    {
                        continue;
                    }
                    b = Inside(b);
                    if (Settings.Destruction > 0)
                    {
                        Kit.Trench(a, b, (3f - 1.5f * Mathf.Clamp01(s0 / far)) * WorldScale, 4, null, false);
                        Lava(a, b);
                        if (((int)(s0 / 5f) + i) % 4 == 0)
                        {
                            ScarDisk(b, 5f * WorldScale, 0.15f);
                        }
                    }
                    DoomAround(b, 3f);
                    if (Vis(b, 1f))
                    {
                        PrimFx.Crack(a, (b - a).normalized, (b - a).magnitude, 3f * _px, 1.2f, new Color(1f, 0.45f, 0.15f, 1f), FxLayer.Ground, 4, false, false, 0);
                        Spec.Rocks(b, _px, 4, 90f, _k);
                    }
                }
            }
            return reach >= far && _bt > 2.5f;
        }
    }
}
