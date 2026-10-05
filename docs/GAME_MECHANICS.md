# Game Mechanics

This file defines the **names** of things in Necromancer and **how they interact**.
Claude uses these exact terms in code (class names, variables, comments), so keep them consistent.

---

## Glossary

One line per term. Add to this whenever you name something new.

| Term | Category | Definition |
|---|---|---|
| Lich | Character | The player's main character: a once-powerful undead mage whose spirit is bound to the Phylactery. |
| Phylactery | Building | The vessel holding the Lich's spirit, housed in a ritualistic altar. The starting building; if it's destroyed, the player loses. |
| Minion | Unit | Any selectable, controllable player unit (cultists, wraiths, skeletons, etc.). |
| Cultist | Minion | Starting worker and builder. |
| Wraith | Minion | A phantom scout. High movement speed and a long vision radius. Can cross water and forest. |
| Skeletal Warrior | Minion | Basic melee soldier. |
| Skeletal Archer | Minion | Basic ranged soldier. |
| Church of the First Flame | Faction | The human enemy: a theocracy ruled by fire mages. |
| Townsperson | Enemy | Common human of the Church's settlements. Weak. |
| Acolyte | Enemy | Trained soldier of the Church. |
| Flame Priest | Enemy | Fire mage of the Church. The most dangerous human unit. |
| Tick | Time | One game minute; one real second. The smallest step of the game clock. |
| Game hour | Time | 60 ticks; one real minute. The unit for production rates. |
| Day | Time | 24 game hours: one day/night cycle. |

Categories might include: Unit, Building, Resource, Ability, Faction, Stat, Status Effect, Terrain, UI.

---
## The Setting

The world is either undeveloped (wild, dense forested land, swamp, with dangerous creatures) or populated by humans. The humans make small outposts with some kind of church, a few houses, small farms with fences or walls surrounding them. They are ruled by mage-priests who harvest energy fonts to make flame - to ward off evil beasts, to warm and light the outposts, and to secure their own status and power. The lower humans produce food through farming, hunting, or foraging. The world has seen constant war for thousands of years, as different factions of magic users control and suppress other beings in the pursuit of power. 


## Player

The player is a lich, spawning from a phylactery. They were once powerful, but their physical form was destroyed and all that's left is a portion of their spirit wandering the Earth. A small group of cultists have obtained a phylactery and channeled the player's spirit into a random place on the outskirts of civilization. The players' goal is to renew their strength, manifest a physical presence in the world, grow an army of minions, construct infrastructure to grow in magical and military might, until they can subjugate or eliminate all human presence on the map. While their power grows, the enemy mages detect their presence, and send larger and larger raiding parties to try to destroy them again. 

The match continues until one side is wiped out:

- The player **wins** when all enemy forces on the map have been destroyed, including a final onslaught of humanity.
- The player **loses** if enemy forces reach and destroy the Phylactery.

A typical match should last about 3 days (72 game hours), with a hard limit of about 84 game hours (see Game time).

The player controls **minions**: wraiths, skeletons, revived remains, cultists, corrupted mages, and abominations. The player commands those minions to fight and build for them. The player should be constantly fighting for more space on the map, which lets them harvest more resources and grow their economy, but also increases the difficulty of enemies they encounter and increases the size of enemy incursions. 

Space is a valuable resource. Players need to be rewarded for efficient placement of buildings, which will all be different geometric collections of hexes as well as having terrain type or adjacency restrictions.

## Resources

<!-- Name, how it's gained, how it's spent, any cap. -->

| Resource | Gained by | Spent on | Notes |
|---|---|---|---|
|Wood|Sawmill placed adjacent to forest hexes|Early structures will have a flat wood price, other structures will have wood upkeep to burn as fuel| |
|Granite|Quarry placed on ground with granite features| Granite is a building material for tier 1.5 and onward| |
|Black Basalt|Quarry placed on ground with black basalt features| Basalt will be the tier 2.5+ building cost| |
|Iron|Produced in a Bloomery, either on iron ore veins or adjacent to swampland|Weaponry and upgrades| |
|Ichor|Harvested from Eldritch Veins, something like springs of bubbling ichor arising from buried elder gods|Minions and research| |
|Anima|Produced by Soul Font buildings placed on terrain with rising, wispy anima fumes|Minions |Anima is like the raw material a soul is made from. All necromantic creations have an anima supply cost. This functions like supply in starcraft. The Soul Fonts will have a multiplier that increases based on other soul fonts within range, creating a network of ghostly beams.|
|Ostite|Produced by Bone Pits |Ostite will function like gold/currency in other rts games. Almost everything will have an ostite cost. The buildings will all have a skeletal foundation or inner structure to justify their ostite cost.| Bone pits are constantly delivering more ostite. Bone pits are especially productive when placed on graveyards|

## Units

<!-- Copy this block for each unit. -->

### *Unit name*
- **Role:**
- **Cost:**
- **Created by:**
- **Stats:** health · speed · damage · range · vision radius 
- **Abilities:**

### Cultist
- Starter worker / builder / weak attacker
- N/A
- A typical map spawns with 6.
- 10 · 2 · 1 · 1 · 5 
- N/A
  
### Wraith
- Phantom / scout
- Crypt
- A typical map spawns with 2.
- 3 · 4 · 0 · 1 · 8
- Wraiths can travel over water and through forests. 

### Skeletal Warrior
- Grunt Melee Footsoldier
- 1 anima supply · 10 Ostite 
- Crypt
- 20 · 2 · 3 · 1 · 6
-

### Skeletal Archer
- Ranged attacker
- 1 anima supply · 8 Ostite · 5 wood
- Crypt
- 12 · 2 · 2 · 5 · 7 *(placeholder stats: fragile but hits from range)*
- N/A

## Buildings

### *Building name*
- **Size:**
- **Cost:**
- **Produces / unlocks:**
- **Placement requirements**
  
### Phylactery
- 1 radius hexagon (7 total tiles) starting building: the Lich's vessel, housed in a ritualistic altar.
- N/A
- The phylactery has building menus, upgrades to technology tiers.
- The phylactery can not have any building immediately adjacent to it.
  
### Bone Pit
- 4 hexes in a diamond (a 2×2 rhombus). Can be tessellated immediately adjacent to other bone pits. Neighboring bone pits join into larger/deeper pits that produce more ostite. 
- 30 ostite. 
- Bone pits are fundamental currency(ostite) generators. They will have tiered upgrades. Tier one produces 8 ostite per hour.
- Bone pits can be placed on flat, resourceless ground tiles.
  
### Soul Font
- 3 tiles that share a vertex. They produce a tower in the center vertex with a glowing purple sphere.
- 75 ostite, 10 wood. 
- Each soul font adds 8 to the anima cap (allows you to train more units)
- Soul Fonts can be placed on flat, resourceless ground tiles. Soul Fonts can not be placed within 5 tiles of another soul font. 
  
### Crypt
- 4 hexes in a diamond (a 2×2 rhombus).
- 20 wood, 100 ostite. *(Was "20 stone": there's no stone resource, and granite only starts at tier 1.5, so the stone cost became extra wood.)*
- The crypt is a tier 1 barracks building, for building basic warriors.
- Placement: flat, resourceless ground tiles. *(Assumed, matching the other basic buildings.)*

## Abilities & spells

### *Ability name*
- **Used by:**
- **Cost / cooldown:**
- **Effect:**

## Enemies & factions

Church of the First Flame: a theocratic organization run by fire mages. They have small churches throughout the world, near clusters of houses and farms.

Enemy units (stats to be decided):

- **Townsperson:** common folk who farm, hunt and forage. Weak fighters, found in every settlement.
- **Acolyte:** the Church's trained soldiers. They guard chapels and make up most raiding parties.
- **Flame Priest:** fire mages who lead the Church and harvest energy fonts. Rare and dangerous.

## Game time

The game clock tracks the passage of time. It drives production rates and the day/night visuals.

| Game time | Real time |
|---|---|
| 1 game minute (one **tick**) | 1 second |
| 1 **game hour** | 1 minute |
| 1 **day** (24 game hours, one day/night cycle) | 24 minutes |
| Typical survival match (3 days, 72 game hours) | 72 minutes |
| Hard limit (about 84 game hours) | about 84 minutes |

- **Ticks:** the clock advances one game minute per real second. Anything measured "per hour" (e.g. a Bone Pit's ostite per hour) is spread evenly across that hour's 60 ticks, so resources trickle in rather than arriving in hourly lumps.
- **Start time:** a match begins at 06:00 on Day 1 (dawn). *(Assumed — change if you prefer.)*
- **Day/night cycle:** daytime is 06:00–18:00 and night is 18:00–06:00. The cycle only changes the visuals (sun angle, light color, darkness at night) and marks time passing. It has no gameplay effects.
- **Match length:** the match runs until all enemy forces are destroyed (win) or the Phylactery is destroyed (lose). Pacing should bring most matches to an end around day 3, with a hard limit of about hour 84 (midday on Day 4).
- **UI clock:** always visible on screen, showing the day and time, e.g. `Day 2 — 14:30`.

---

## Survival systems

<!-- Day/night, hunger, decay, corpses, waves — whatever applies. -->

## World & map

Survival mode will drop the player off at a random point in the middle of a randomly generated map. The start building is 7 hexes (a circle of six around one). The rest of the map will be covered by fog of war. Medium map size: a square with about a 300 tile side length. Buildable area will be grassland or desert. Forest will be unbuildable/impassable. Cliffs will be impassable stone. Water is impassable. Swamp land is passable at a 50% movement speed debuff. 

Grassland and desert tiles should have granite, black basalt, and iron patches randomly spread throughout. 

Starting 30 tiles from the phylactery, the map should be populated by followers of the church of the first flame. This means some isolated farms with weak/little defenses, small groups of buildings like houses/blacksmithes around a chapel, and rarely a large city, with a church, many houses, and walls. 

---

## Interactions

How the parts above affect each other. Write these as simple rules —
they become the logic Claude implements.

Format: **When** *something happens* → **then** *result*.

- **When** enemy forces destroy the Phylactery → **then** the player loses.
- **When** all enemy forces on the map are destroyed → **then** the player wins.
- **When** a Bone Pit is placed adjacent to another Bone Pit → **then** they join into a larger, deeper pit that produces more ostite.
- **When** a Bone Pit is placed on a graveyard → **then** it produces extra ostite.
- **When** a Soul Font is within range of other Soul Fonts → **then** its output multiplier increases, shown as ghostly beams between them.
- **When** a minion is created → **then** it uses up anima supply; if the anima cap is reached, no more minions can be created until it's raised.
- **When** the player claims more of the map → **then** enemy encounters get harder and raiding parties get larger.
- **When** a minion enters swamp → **then** it moves at 50% speed.

---

## Open questions

- Is the Lich a unit on the map from the start, or only the spirit in the Phylactery until it "manifests a physical presence"?
- What happens if the hard limit (about hour 84) is reached before either side is wiped out?
- When does the final onslaught of humanity arrive: at a set time, or when the player reaches a certain size?
- Stats for Townsperson, Acolyte and Flame Priest.
