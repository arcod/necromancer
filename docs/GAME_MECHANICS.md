# Game Mechanics

This file defines the **names** of things in Necromancer and **how they interact**.
Claude uses these exact terms in code (class names, variables, comments), so keep them consistent.

---

## Glossary

One line per term. Add to this whenever you name something new.

| Term | Category | Definition |
|---|---|---|
| *Necromancer* | Unit | *(example — replace)* The player's main character. |
| | | |

Categories might include: Unit, Building, Resource, Ability, Faction, Stat, Status Effect, Terrain, UI.

---

## Player

<!-- What does the player control? How do they win or lose? -->

- **Win condition:**
- **Lose condition:**
- **Controls:** <!-- select, move, attack, cast, build... -->

## Resources

<!-- Name, how it's gained, how it's spent, any cap. -->

| Resource | Gained by | Spent on | Notes |
|---|---|---|---|
| | | | |

## Units

<!-- Copy this block for each unit. -->

### *Unit name*
- **Role:**
- **Cost:**
- **Created by:**
- **Stats:** health · speed · damage · range · <!-- others -->
- **Abilities:**
- **Strong against / weak against:**

## Buildings

### *Building name*
- **Purpose:**
- **Cost:**
- **Produces / unlocks:**

## Abilities & spells

### *Ability name*
- **Used by:**
- **Cost / cooldown:**
- **Effect:**

## Enemies & factions

<!-- Who opposes the player? How do they behave? -->

## Survival systems

<!-- Day/night, hunger, decay, corpses, waves — whatever applies. -->

## World & map

<!-- Map size, terrain types, fog of war, procedural vs. handmade. -->

---

## Interactions

How the parts above affect each other. Write these as simple rules —
they become the logic Claude implements.

Format: **When** *something happens* → **then** *result*.

- **When** *(example)* a living unit dies near the Necromancer → **then** it leaves a corpse that can be raised.
-

---

## Open questions

-
