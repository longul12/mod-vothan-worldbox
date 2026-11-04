using UnityEngine;

namespace Sakuga
{
    /// <summary>
    /// How an enemy stands right now, as a fighter sees it (Ctx.State): what it is doing (the Forms
    /// of its move, or of the blow it just threw), and whether it is gathering power (break it),
    /// guarding (grab it, kick it), in the air (anti-air), down (finish it), caught recovering from
    /// an answered attack (punish it), stunned, busy in a move of its own, a shooter or a caster
    /// keeping its distance (close in or deflect), and whether its attack is aimed at the one looking.
    /// </summary>
    internal struct FoeState
    {
        public Form Doing;
        public bool Charging, Guarding, Airborne, Down, Stunned, Recovering, Busy, Ranged, AimsAtMe;

        public static FoeState Of(Actor v, Actor looker)
        {
            FoeState s = default;
            if (v == null || !v.isAlive())
            {
                return s;
            }
            s.Doing = Reads.Doing(v);
            s.Charging = Moves.Charging(v);
            s.Guarding = (s.Doing & Form.Guard) != 0;
            s.Airborne = v.isFlying() || Combat.HeightOf(v) > 0.6f;
            s.Stunned = v.is_unconscious || v.hasStatusStunned();
            s.Down = s.Stunned && !s.Airborne;
            s.Recovering = Reads.Recovering(v);
            s.Busy = Moves.Committed(v) && !s.Charging;
            s.Ranged = (Preys.Of(v) & (Prey.Ranged | Prey.Caster)) != 0;
            s.AimsAtMe = looker != null && Reads.AimsAt(v, looker);
            return s;
        }

        public override string ToString()
        {
            return "doing " + FormSet.Name(Doing) + (Charging ? " CHARGING" : "") + (Guarding ? " guarding" : "") + (Airborne ? " airborne" : "")
                + (Down ? " down" : "") + (Recovering ? " recovering" : "") + (Busy ? " busy" : "") + (Ranged ? " ranged" : "") + (AimsAtMe ? " aims-at-me" : "");
        }
    }
}
