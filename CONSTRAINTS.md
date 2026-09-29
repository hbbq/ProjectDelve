# Project Delve — Constraints

This document captures the foundational constraints for Project Delve. It is intentionally focused on the physical game and its representation, not on gameplay rules that have not yet been decided.

The digital implementation is a reference implementation of a physical board game. The physical game is the source of truth for what must be possible.

## Design principles

### Physical first

Every mechanic must be possible to perform and represent in the physical game.

The digital implementation may automate rules and calculations, but it must not introduce capabilities that only make sense because a computer is available. Randomness, state changes, positioning, and other mechanics must have a plausible physical equivalent.

The digital representation models the physical game. The physical game must not need to adapt to shortcuts or limitations in the digital implementation.

### Explicit rules

Implementation details must not silently become game rules.

If the software needs to know how a situation is resolved, the corresponding rule should be made explicit. Ambiguities discovered while implementing the game are opportunities to clarify or revise the game design.

### Physical State is a first-class model

Physical State describes the observable physical situation on the table without interpreting what it means according to the rules.

It includes, at minimum:

- the board layout,
- the terrain tile at each grid position,
- physical features on edges,
- physical state of changeable features such as doors,
- which figures are on the board,
- the grid position of each figure,
- the physical posture of each figure.

Physical State is separate from Rules/Game State.

### Rules/Game State is separate

Rules/Game State contains information required by the game rules that is not simply the physical layout of the board.

Examples may eventually include health, actions, abilities, experience, activation state, and turn state.

Exactly what belongs here will be defined as the rules develop.

### Presentation is separate

Physical State and Rules/Game State must not depend on a particular renderer.

The same state should in principle be representable as, for example:

- a simple textual or ASCII representation,
- a 2D graphical representation,
- a 3D representation,
- the actual physical board and components.

## Board

The board is an orthogonal grid of square cells.

There is no fixed board size. A typical scenario is expected to be around 15 × 10 cells or smaller, but the model must not impose that as a maximum. Larger boards and different proportions must remain possible.

There are no meaningful positions between cells.

## Terrain tiles

Every grid cell contains exactly one terrain tile.

Terrain tiles currently fall into two broad physical categories.

### Flat tiles

A flat tile has no physical geometry preventing a figure from being placed on it.

Examples include:

- grass,
- stone,
- water,
- tiles with printed symbols or special markings.

Being physically able to place a figure on a tile does not imply that the game rules allow the figure to occupy or move onto it.

### Tiles with fixed objects

A tile may have a three-dimensional physical object permanently attached to it.

Examples include:

- trees,
- tables,
- chests,
- buildings.

If the physical object occupies the space needed by a figure, a figure cannot occupy that cell.

This is a physical constraint, independently of any gameplay meaning the object may later have.

## Edges

The boundary between two orthogonally adjacent cells is a single shared edge in the game model.

An edge may contain physical features such as:

- a wall,
- a door,
- an opening such as a window.

The exact taxonomy and gameplay effects of edge features are not yet defined.

Walls and similar features may physically be attached to one of the adjacent terrain tiles, but this construction detail does not change the logical model: the edge is shared by both cells.

A corner tile may therefore physically carry features on two of its edges.

### Doors

Doors are physically capable of being opened and closed.

Their current physical state is therefore part of Physical State.

The gameplay consequences of an open or closed door are rules and are deliberately not defined here.

### Windows and openings

Walls may contain openings such as windows.

Their effects on movement, line of sight, attacks, or other rules are deliberately deferred.

## Figures

### v0 footprint

The initial implementation supports only figures with a 1 × 1 footprint.

Larger figures are expected in the future, so the overall model should not assume that all figures must always be 1 × 1. Support for larger footprints is not required in v0.

### Position

A 1 × 1 figure on the board occupies exactly one grid cell.

Two figures cannot occupy the same cell.

A figure cannot occupy an intermediate position between cells or partially occupy multiple cells.

How movement between cells works is a gameplay rule and is not defined here.

### Facing

Figures have no gameplay-facing or orientation.

A physical miniature may visually face any direction, but its rotation has no meaning in the game state.

### Physical posture

A figure can physically be in one of two postures:

- upright,
- lying.

This posture is part of Physical State because it is directly observable on the table.

The gameplay meaning of either posture is deliberately not defined here.

## Units

Heroes and Monsters are both Units and use the same underlying rules model.

A Unit has:

- the common Unit stat schema,
- a set of abilities,
- Rules/Game State associated with the Unit,
- a physical figure when the Unit is present on the board.

Heroes and Monsters should not have parallel implementations of the same mechanics.

### Heroes and Monsters

The fundamental distinction between a Hero and a Monster is agency, not mechanics.

For a Hero, a player makes decisions among the choices permitted by the rules.

For a Monster, decisions are determined by that Monster's behavior rules. Monster behavior may determine, for example:

- movement choices,
- target selection,
- which ability to use,
- special decision overrides.

Monster behavior must be possible to follow manually in the physical game. It must not depend on opaque computer-only decision making.

Random choices or outcomes may be used only where the rules specify a corresponding physical random mechanism.

### Abilities and behavior

Abilities define what a Unit can do or what effects it can produce.

Monster behavior defines how a Monster makes decisions about those capabilities.

These are conceptually separate even if a physical Unit card presents behavior text next to, or as part of, an ability for readability.

## Unit stats

Every Unit has the same five base stats:

- `MOV` — Movement,
- `RNG` — Range,
- `ATK` — Attack,
- `DEF` — Defence,
- `HP` — Hit Points.

All five stats are integers.

Their base domains are:

- `MOV >= 0`. A Unit with `MOV 0` cannot move.
- `RNG >= 0`. A Unit with `RNG 0` cannot make a normal attack.
- `ATK >= 0`. A Unit with `ATK 0` cannot make a normal attack.
- `DEF >= 0`. `DEF 0` is a normal valid value.
- `HP >= 1`. Zero is not a valid HP stat value.

The HP stat and current HP are distinct concepts. Current HP is mutable Rules/Game State. Current HP cannot be reduced below zero. Under the normal rules, a Unit whose current HP reaches zero dies and its figure is removed from the board. Future abilities may explicitly modify this behavior.

The detailed gameplay meaning of the stats is defined by the corresponding rules rather than by these domain constraints.

## Unit activation

Heroes and Monsters use the same activation structure.

A Unit activation always progresses through three phases in this fixed order:

1. **Bonus Action**
2. **Move**
3. **Act**

A phase still occurs when the Unit chooses to do nothing during that phase. This is significant for rules that may trigger before or after a phase.

### Bonus Action

During the Bonus Action phase, a Unit may use zero or one ability explicitly marked `Bonus Action` on its Unit card.

There is no general set of Bonus Actions. A Unit can perform a Bonus Action only when its card provides an applicable Bonus Action ability.

If several Bonus Action abilities are available, at most one may be used during the activation.

For a Hero, the player chooses whether and which available Bonus Action to use. For a Monster, its behavior rules determine that choice.

### Move

During the Move phase, a Unit moves from zero through `MOV` steps.

Each step moves to an orthogonally adjacent cell. Diagonal movement is not allowed.

The complete movement is one atomic rules operation. The path may contain multiple cells and is relevant when determining whether the movement is legal, but gameplay effects do not occur between individual steps of the movement.

Rules may therefore trigger before or after the complete movement, but not in the middle of it unless a future rule explicitly overrides this principle.

Choosing zero steps still constitutes completing the Move phase and the movement operation. Consequently, a future rule triggered after movement may still trigger when the Unit moved zero steps.

Each movement step crosses the shared edge between the current cell and an orthogonally adjacent destination cell. The step is legal only if that edge is passable for movement.

For the currently defined edge features:

- a wall is impassable,
- a closed door is impassable,
- an open door is passable.

Other edge features may define their own movement passability when introduced.

A Unit may pass through cells occupied by friendly Units during its movement.

A Unit may not pass through cells occupied by hostile Units. Hostile Units can therefore block movement paths.

A Unit may never end its movement in a cell occupied by another Unit, whether friendly or hostile.

Whether a destination cell is otherwise passable is determined by the rules for its terrain or fixed object. Those rules are deliberately deferred to the corresponding terrain specification.

### Act

During the Act phase, a Unit may perform zero or one Action.

All available Actions compete for the same single Action opportunity. A Unit cannot perform a basic Action and a Unit-specific Action during the same Act phase unless a future rule explicitly overrides this limit.

A normal **Attack** is a basic Action available through the general rules.

A Unit card may provide additional abilities explicitly marked `Action`. Using one of these consumes the Unit's Action for the phase.

Other basic Actions may be introduced by the general rules. For example, opening a nearby door is a possible future basic Action, but this has not yet been decided.

## Normal attacks

A normal Attack is a basic Action.

A Unit can make a normal Attack only when both its effective `RNG` and effective `ATK` permit it. Base stats may be modified by future rules or abilities; attack resolution uses the resulting effective stat values. The rules for combining modifiers are deliberately deferred until needed.

### Range

For ranged attacks, `RNG` is the maximum orthogonal distance between attacker and target. Orthogonal distance is Manhattan distance: the absolute horizontal difference plus the absolute vertical difference between their cells.

`RNG 1` is a special case called **Melee**. A Melee attack may target any of the eight cells immediately surrounding the attacker, including diagonally adjacent cells. A physical Unit card may display `Melee` rather than the numeric value `1` to make this distinction explicit.

A target must satisfy both the applicable range rule and Line of Sight.

### Line of Sight

Line of Sight (LOS) is a straight geometric line from the center of the attacker's cell to the center of the target's cell.

Range measurement and LOS are separate tests.

LOS blocking is based on logical grid geometry rather than the detailed physical silhouette of a miniature or terrain model.

A cell is either completely LOS-blocking or completely non-blocking. If a type of terrain or fixed object is defined as LOS-blocking, the entire area of its cell blocks LOS regardless of the object's actual physical shape within the cell.

Likewise, an edge feature either blocks LOS at the point where the LOS line crosses that edge or does not. For example, an open door does not partially obstruct an edge merely because a physical door model remains present.

Because LOS runs between cell centers, it cannot run along a grid edge. It can, however, pass exactly through a grid corner.

When LOS passes exactly through a corner, it is blocked only when all possible passages through that corner are blocked. If at least one passage is free, LOS passes through the corner. This also applies when a wall terminates exactly at the corner.

Merely touching the corner of an LOS-blocking cell does not count as passing through that cell's interior.

Friendly Units do not block LOS.

Whether hostile Units block LOS is deliberately not yet defined.

Which terrain types, fixed objects, walls, doors, windows, and other edge features block LOS will be defined by their corresponding rules.

### Attack and defence dice

The attacker rolls a number of Attack Dice equal to its effective `ATK`.

An Attack Die is a physical six-sided die with:

- 3 Hit faces,
- 3 Miss faces.

The defender rolls a number of Defence Dice equal to its effective `DEF`.

A Defence Die is a physical six-sided die with:

- 2 Block faces,
- 4 Miss faces.

Count the total Hits and Blocks rolled.

Damage is:

`max(0, Hits - Blocks)`

The defender's current HP is reduced by the resulting Damage, but never below zero.

A Unit with effective `ATK 0` cannot make a normal Attack. `DEF 0` is valid and means that the defender rolls zero Defence Dice.

### Death

Under the normal rules, when a Unit's current HP reaches zero, that Unit dies and its figure is removed from the board.

This rule applies equally to Heroes and Monsters.

Future abilities or special rules may explicitly alter what happens when a Unit would die, but no such exceptions are part of the base rules yet.

## Physical component constraints

The abstract game model and the available physical components are separate concerns.

The rules may permit a world configuration that cannot be constructed using the currently manufactured set of terrain pieces.

For example, a wall edge may be valid in the abstract model, while no physical water tile with an attached wall exists. With the current component set, that means such a map configuration cannot be physically constructed.

Component availability should therefore constrain physical scenario construction rather than silently becoming a universal game rule.

This separation also allows the physical component set to grow without requiring fundamental rule changes.

## Loose tokens and markers

No loose tokens or markers are currently planned as part of the board representation.

They may be introduced later. The architecture should not unnecessarily prevent their addition, but v0 does not need a generic token system.

## Explicitly deferred

The following are intentionally not specified yet:

- gameplay effects of terrain,
- terrain and fixed-object movement passability rules,
- LOS-blocking behavior of hostile Units,
- exact LOS effects of terrain and edge-feature types,
- stat modifier and effective-stat calculation rules,
- gameplay effects of walls, windows, and doors,
- gameplay meaning of upright and lying figures,
- passing through occupied cells,
- larger-than-1×1 figure behavior,
- loose tokens and markers,
- exact terrain and edge-feature taxonomies,
- scenario validation against an inventory of physical components.

These should be introduced only when the corresponding game rules or content require them.
