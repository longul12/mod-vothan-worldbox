# Combat Gods

Nhánh phát triển Việt hóa/bổ sung **Võ Đạo Chiến Thần 1.1.3**, dựa trên mod của **Turbo**.

Huyền Nguyệt phái đang phát triển từng bước: [lộ trình và cách thử 18 thế Novice–Expert](HUYEN_NGUYET_ROADMAP_VI.md).

- [Tiến độ nhánh và hướng dẫn kiểm tra](PROGRESS_VI.md)
- [Điều kiện Chân Thần và Cửu Trọng Lôi Kiếp](CHAN_THAN_VI.md)
- [Thuật ngữ Việt hóa](GLOSSARY_VI.md)

Phần mô tả gốc bên dưới được giữ để tham khảo; thay đổi riêng của nhánh này được ghi trong các tài liệu tiếng Việt ở trên.

Anime combat for WorldBox. Fighters climb a combat ladder from **Combat Novice** to **Combat God**,
learn techniques at levels I, II and III, read the battlefield, chain techniques into combos, fight
long duels, team up, and at the very top stop the world with God abilities. Everything is animated
frame by frame in the game's own pixel art: squash and stretch, smears, afterimages, swooshes,
dust, craters, shockwaves, hit-stops and impact frames, drawn at the pixel size of the unit making them.

## Combat tiers

| Tier | Colour | Fights techniques against | Knows |
|---|---|---|---|
| Combat Novice | white | people, wolves | its school's Novice techniques |
| Combat Adept | blue | people, wolves | + its school's Adept techniques |
| Combat Expert | yellow | bears and brutes | + Expert techniques |
| Combat Master | orange | demons and fiends | + Master techniques and the school's first great set pieces |
| Grandmaster | red | dragons and titans | + Grandmaster techniques; pressure aura |
| Combat God | gold and black | anything, Crabzilla included | its whole school at III, the school's God techniques, and the God abilities that fit its school |

- A technique is level I at the tier that learns it, II one tier later and III two tiers later.
  Level I is white and quick, level II takes the tier's colour, level III burns white-hot with a
  glowing crater and freezes the world on impact.
- Tiers add health, damage, armor, speed, crits, stamina and **mana**. Techniques cost mana; higher
  tiers have more of it, regenerate faster and have shorter cooldowns.
- **Weight classes:** a Novice only uses techniques on people and wolves, an Expert takes on bears,
  a Master demons, a Grandmaster dragons, a God anything. At 30% health or less a fighter may punch
  one class above itself, at half damage. Ordinary attacks are never limited.
- **Every civilized people fights:** humans, elves, orcs, dwarves and all 35 animal civilizations
  (cat folk, bear folk, crab folk, frog folk, crystal golem folk, unicorn folk...) grow fighters, use
  every technique and count as people in a fight, whatever their kind's health. Each kind leans to
  the schools that suit it (bears to Iron Fist, rabbits to Tempest Leg, foxes to Phantom Step,
  unicorns to Ki Arts...).
- Giving a tier replaces any other tier (they never stack).
- **Long lives:** Experts live 125 years longer than their people, Masters 250, Grandmasters 500.
  Combat Gods and Primordials never die of old age (their lifespan shows as ???): only a fight can end them.
- **Power scales with the tier.** Health, speed and damage climb steeply (a God has thousands of
  health and hits about two and a half times as hard as a Novice with the same move), and so does
  the destruction a technique leaves behind.

## Combat schools

Every fighter walks one school, from Novice to God; climbing a tier deepens its mastery of that school
(stronger, cheaper, quicker techniques) instead of adding others. A player can still give a unit any
school's trait, or every one of them. Each school fights only in its own way and with its own pictures,
from its first jab to its God's catastrophe:

- **Iron Fist** — punches, flurries, fists of pressure flying through lines, a wall of fists, fist rain,
  and at the top a fist the size of a hill punched down out of the sky.
- **Tempest Leg** — kicks, wind crescents, cyclones and typhoons, lightning, sky ladders and falling-star heels.
- **Thousand Blades** — cuts, iai draws, sword waves (from the ground and down from the air), the domain of
  cuts, the sword gate, sword rain, a cut of light across the land, a sword the size of a tower.
- **Ki Arts** — palms, ki bullets, homing orbs, spears of light, beams, domes, pillars of light, a sun of ki.
- **Earth Breaker** — brawling, grabs and throws, boulders and rock sprays, pillars, quakes, fissures,
  rockfall, a burning mountain out of the sky.
- **Phantom Step** — blinks, afterimages and clones, legions, returning shadow discs, blades of shadow
  raining out of the dark, the void sphere.
- **Hunter's Way** — spear thrusts, javelins that pin, arrows and arrow rain, harpoons, stake traps,
  pounces, the javelin gate, a spear thrown down from the heavens.

Only fists and earth set the land on fire; the other schools scorch, dig and cut it. Techniques of no
school are only learned by the schools they suit (a swordsman never calls a meteor).

## Flow fighting (Masters and up)

Masters, Grandmasters and Gods never swing a plain weapon. Between techniques they fight in one
continuous flow: strike chains (lunges, rising cuts, sweeps, launch kicks, cleaves), ki shots at
enemies they can't reach, blur dashes, and blade-on-blade exchanges when two of them meet. The higher
the tier, the shorter the breaths between techniques. They wade through shallow water, and fire
doesn't touch them.

## Moves (each is also a single trait)

Give any move on its own as a trait: the unit gets just that move, no stats, at the **bare move
level** (setting, default III).

- **Flash Step** — vanish and reappear past the enemy; the cut lands a beat later. III zig-zags.
- **Cyclone Heel** — a spinning heel kick that clears space. III lifts enemies into a tornado.
- **Hundred Fists** — a flurry too fast to count, then a launch. III turns the victim into a bowling ball.
- **Rising Tempest** — kick it into the sky, juggle it, spike it into the ground. III adds the Tempest Drop.
- **Heaven Splitter** — an uppercut that sends the enemy into the sky. II launches it far into a crater; III
  whirls around it in a storm of wind, then rockets it off the screen to crash down far away (a
  geyser if it lands in water). A God's sends it across the map.
- **Meteor Slam** — leap, flip, dive as a burning meteor, crater the crowd. III leaves the screen first.
- **Still Water** — parry a technique; the attacker staggers and eats a riposte. Casual sword parries too.
- **Mirage Step** — an afterimage takes the hit while the real fighter is already behind you.
- **Hurricane Swing** — grab an enemy, swing it like a hammer, bowl it into its friends.
- **Crescent Fang** — the cut leaves the blade and flies. III: Moonfall, everyone in its path bursts at once.
- **Severing Flash** — iaido: already behind you, a lone wounded enemy falls in two.
- **Fault Line** — a fist into the earth; a fissure races out and erupts under a line of enemies.
- **Wingbreaker** — leap to whatever rules the sky and drive it into the earth; Skull Cracker on dragons.
- **Phantom Rush** — vanish, cut every scattered foe in one breath, sheathe: all the wounds open at once.
- Team techniques (need allies nearby): **Sky Relay** (friends pass a kicked enemy around the circle
  until the last one ends it, at III with a fireball), **Cross Fang** (two or four fighters dash
  through one enemy at once), **Unison Burst** (up to ten fighters pour their auras into one, who lets
  it all go as one wave).

## Dynamic battles

- **A tactical mind.** Fighters look around a few times a second (faster at higher tiers) and pick the
  technique that answers the situation: a crowd gets a Meteor Slam, a line a Fault Line, a lone
  wounded enemy a Severing Flash, an archer a Flash Step, a flier a Wingbreaker. Low tiers are eager
  and clumsy; high tiers judge well, don't waste power, and chain techniques into combos
  (a launch into a juggle into a slam). Nobody repeats themselves for long, and allies don't drop two
  slams on the same crowd.
- **Duels.** Two worthy enemies who meet stare each other down, clash, and fight a long duel of
  varied exchanges (blade locks, flurries, aerial clashes, chases, feints, ground shakers), with
  parries and counters between them, until a final exchange. Survivors become rivals and seek each
  other in later wars. Grandmasters and Gods unleash catastrophes in their duels: a fall from the
  sky, a tectonic clash, a battle in the clouds, a mountain broken in half, a relentless barrage.
  Two Gods most often open with the Clash of Heavens, and their fight crosses the whole map.
- **The world takes the damage.** Techniques scorch, crater and split the ground, set the grass
  alight around them and wreck enemy buildings; a God's fight leaves a scarred country behind it
  (setting: off, scorch marks only, full).
- **A master easily beats a novice:** much stronger fighters turn ordinary blows aside one-handed.
  Grandmasters and Gods radiate a pressure aura that makes lesser fighters falter. Experts and above
  get a Second Wind once per battle.
- **Teamwork.** Allies gang up on an enemy far stronger than any of them, mark it, and chain their
  techniques across units.
- **Situations.** Last stands, bodyguards who blink in to block a blow, champions of two armies
  seeking each other, rescues, routs when a God mows down a line, vengeance for a fallen comrade,
  masters whose students copy the technique they just saw, challenges, and underdog kills.

## Turning shots back

A projectile that strikes a tiered fighter (an arrow, a fireball, an alien's plasma bolt) may be
turned aside with one hand, and the shot flies back at whoever fired it: 12% for a Novice, 22% Adept,
35% Expert, 55% Master, 75% Grandmaster, 90% God, more against a far weaker shooter. From Master up
it goes back charged, faster, bigger and glowing in the fighter's colour, and bursts where it lands:
harder and wider the higher the tier, a God's tearing the ground open.

Fighters are also hard to rattle: whatever the game does to stun a unit (an explosion's blast, a stun
weapon, a spell) or to knock it flying when a blow lands, a tiered fighter may shrug off: Novice 10%,
Adept 20%, Expert 30%, Master 40%, Grandmaster 50%, God 60%. A stronger realm bypasses this resistance.
A lower realm cannot stun or launch a higher realm unless at least five nearby enemies focus it and
cause Body Break. It still takes the
damage. The player's own powers (bombs, Force) still move everyone.

## Combat Gods

A God fills a **God gauge** in deep battle (hits, kills, fallen allies, big crowds). When it is
full the world stops for a God ability, chosen by the situation (each fits some schools: Starfall
the ki, fist and earth Gods, Zero Moment the sword and phantom Gods, Last Light every God):

- **Starfall** — a crowd: the God rises, the sky turns, meteors rain on every cluster and the God
  dives last as the sun.
- **Zero Moment** — a colossus or a rival: everything freezes and goes dark; twelve strikes from
  every side, then all the cuts open at once.
- **Last Light** — a killing blow: the God kneels, its heart beats twice, it rises in a gold pillar
  and bursts back to life, awakened.

**The Kaiju Killer.** When Crabzilla appears in a world with a Combat God, after a short grace
period the God looks to the sky, flies across the map, takes Crabzilla apart with a hundred strikes,
dives through it as a falling star and kicks its death-nuke into the sky, where it bursts like
fireworks. (Setting; when off, tiers fight Crabzilla like anyone else.)

## Primordial (above the Gods)

The **Primordial** trait (in the Combat Tiers group) is the power above Combat God: a planetary threat.
It is never earned or born; only a player can give it. A Primordial keeps everything a God has and
adds:

- **The Awakening:** giving the trait is not a God's ascension. Once the trait window closes, the camera
  goes to the unit and the whole world stops: the sky turns violet-black, everything on screen is
  pulled into it inside a sphere of the void, and an eye opens in the sky. Then a pillar of
  prismatic light erupts, rings of every colour sweep past the edges of the screen, and
  "A PRIMORDIAL HAS AWAKENED" appears, then its name. When the world moves again, everything near
  it is thrown down, every unit in the world turns to look, and a message and a world-log entry
  tell the world.
- **Overwhelming power:** about thirteen times a God's health. Its blows land three times as hard,
  and anything less than a Primordial deals it only a third of its damage.
- **Its own powers** (Primordial Powers group, no one else can use them): Erasure, Annihilation Lance,
  Genesis Wave, Moonfall, Judgement of the First, Spectrum Cataclysm, Supernova, Stillness of the
  First, Crushing Heaven, Chains of Creation, Black Sun, Legion of Echoes and Worldbreaker. They kill
  Gods but leave the world standing. A Primordial walks no school and carries no school's weapon.
  No blow of anything less knocks it back, throws it, stuns it or cuts its power short.
- **Gods against a Primordial:** one or two Gods die. Three or more Gods fighting the same Primordial
  join their power every 16 seconds into one strike that goes through its hide (9% of its life per
  God). Three or four usually fall first; five or more can bring it down.
- **A Primordial takes the initiative:** it turns on whoever attacks it and reaches for its own
  powers first whenever one is ready.
- **World's End:** two Primordials who are not of the same kingdom cannot share a world. They find each
  other and fight across the map like the end of an anime. The fight includes:
  - hyper-rush barrages that send the other flying across the land
  - ten collisions in a heartbeat, each leaving a crater
  - a fight high in the sky that ends with one spiked into the ground
  - a throw that ploughs a lava trench across the land
  - beam struggles
  - circling orb barrages
  - comet dives from out of sight
  - a low chase across the map

  Just moving tears up the world: flying low cuts a trench where nothing survives, and flying high
  sends out shockwaves that flatten the ground below. When the ground under them is gone, they tear
  through space to land that still stands. After every two of these rounds comes a world-ending power:
  Planet Cleaver, Starfall Apocalypse, Continental Collapse, Singularity, Sunfall, World Fire, Tidal
  Apocalypse, Absolute Zero or World Shatter (seven of them in each battle). Then
  comes a final beam struggle, whose wave runs over the whole map. Every unit and building is
  destroyed, and only the winner remains. The **World's End** setting can forbid it.
- **Primordial Screen Shake** setting: camera shake and the big screen flashes while Primordials
  fight or awaken. 0 is none, 1 is a little (the default), 2 is full. It also covers the game's
  own bomb shake during their fights.

## Natural growth

Fighting teaches. Every blow landed on a foe (stronger foes teach more), kills, battles survived,
duels won and above all surviving at the brink of death earn combat XP, and so do the years: warriors
drill, tiered fighters train harder the higher they stand. At those moments, and once a year, a unit
that meets its next tier's gates (XP, kills, level, years at its tier, and for Grandmaster a deed such
as a won duel, a slain demon or a survived brink) may break through, with a ceremony in its tier's
colour. Caps keep the pyramid: many Novices, a few Masters, two Gods (setting). A God's ascension stops
the world.

| To become | XP | Kills | Level | Years at the tier below |
|---|---|---|---|---|
| Novice | 10 | 1 | - | - |
| Adept | 60 | 1 | - | 2 |
| Expert | 110 | 2 | - | 1 |
| Master | 300 | 3 | - | 2 |
| Grandmaster | 700 | 5 | - | 2 (and a deed, or 6 years) |
| God | 1200 | 8 | 3 | 3 (ascension) |

Kills count one more for every 60 XP (a soldier of a long peace still grows, only slower). The years are
at normal growth speed (the default): half again as long at slow, half at fast, a tenth at anime.

Measured on a continent with six young civilizations of ten at sonic speed (two runs): the first
Novice in year 6-7, Adept 14-16, Expert 28-33, Master 46-51, Grandmaster 62-76, God 92-112, and a
pyramid of roughly 150 Novices, 60 Adepts, 10 Experts, 2 Masters, 1 Grandmaster and 1 God.

## Combat Gods tab

- **Novice**, **Adept**, **Expert**, **Master**, **Grandmaster**, **God**, **Primordial**: one button per
  tier. Pick one and click a unit once: it becomes that tier straight away, whatever it was before (the
  unit under the cursor is ringed in yellow, as with the game's own unit powers). A fighter from Adept
  up takes a new tier on its feet; only Novices are knocked out by the change, like any unit in the game.
- **Humble**: click a unit to lower its tier by one (a Primordial falls back to Combat God).
- **Unleash**: click a fighter, it performs the best technique it knows right now.
- **Settings**.

Techniques are never buttons: fighters learn them by climbing the tiers, on their own as the world
goes on or trained by you.

## Auras

Everyone with abilities shows it. A one-pixel outline in the tier's colour hugs the unit, breathes
slowly and burns brighter in a fight, and particles rise off it, more the higher the tier: a faint
silver shimmer on a Novice, blue and yellow motes on Adepts and Experts, orange ki flames on Masters,
crimson flames and crackles with an outer glow on Grandmasters, and on a Combat God a gold ring in a
pale-gold glow with embers, flames and white glints. A unit that knows single techniques only
shimmers like a Novice. Each tier's aura can be switched off in the settings.

## Settings

A **Reset to defaults** button sits at the top of the window. Growth speed (off, slow, normal, fast,
anime; normal by default), highest tier by growth (Novice up to Combat God, the default: a world where
nobody grows past Adept, say), Combat God cap (two by default, so a world can see a duel of Gods), beasts awaken, promotion
announcements, auto-favourite per tier (Masters, Grandmasters, Gods and Primordials by default), an aura switch per tier (all on
by default), battle flares and pressure, hit-stop strength,
God ability cinematics (off, light, full), title cards, tier auras, the spotlight (the camera goes to big
moments by itself: Grandmaster fights and up, God ascensions and Primordials by default; the tier is a
setting), camera follow, bare move
level, technique frequency, technique power, environment damage, mana cost, cooldowns, weight
classes, team techniques, duels, battle situations, the Kaiju Killer, its grace period, and whether
Grandmasters answer Crabzilla.

## Files

- `Code/Moves/*.cs`: one file per technique or system, each with its beat sheet at the top.
- `Code/MeteorSlam.cs`, `Code/RisingTempest.cs`: the first two techniques.
- `Code/Technique.cs`, `Brain.cs`, `Fighter.cs`: techniques, the tactical mind, per-fighter state.
- `Code/Tiers.cs`, `Combat.cs`, `Progression.cs`: tiers, combat rules, natural growth.
- `Code/Moves.cs`, `Fx.cs`, `Art.cs`, `Kit.cs`, `Cinema.cs`, `HitStop.cs`: animation, effects,
  procedural pixel art, cinematics.
- `Code/Settings.cs`, `Traits.cs`, `Powers.cs`, `Hooks.cs`: settings, traits, the tab, events.
- `Code/Auras.cs`: the tier auras.
- `Code/Dev.cs`: developer tools, only active when a `dev` folder exists here.
