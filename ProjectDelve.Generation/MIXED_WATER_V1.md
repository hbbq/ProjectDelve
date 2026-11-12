# Mixed Water Terrain V1

Mixed terrain uses ordinary typed terrain definitions. Wet Grass, Rocky Water and
Stepping Stones permit both land and water travel. Their categories do not decide
movement, LOS or excavation eligibility. Mountain shells still require Solid Rock.

Natural sampling uses the existing absolute-coordinate water shapes and a separate
seed domain, without changing recipe syntax or consuming construction RNG streams.
For intermediate Water coverage (1–99), grass/dirt directly beside sampled Water
has an 18% coordinate-hash chance of Wet Grass. Additional Wet Grass patches use
24×24 world owner squares: 8% of owners receive one circular patch with radius
1–2 Cells, clipped to grass/dirt. Rocky Water uses 16×16 owner squares with a 15%
owner chance and radius 1–2 Cells, clipped to sampled Water. These are sparse
natural clusters; a cluster can span a narrow waterway, but no island connection is
promised. Zero and 100 Water coverage retain their pure endpoint policies.

Patch centers, radii and owner selection are coordinate-derived. Sampling includes
a one-Cell water-feature halo for shoreline decisions, including signed coordinate
limits. Natural crops agree at overlapping coordinates. Crop-local enrichment and
construction remain separate operations and may depend on their finite window.
The pipeline supplies the selected content catalog to sampling. Missing Wet Grass
retains the original grass/dirt choice; missing Rocky Water retains Water. The
sampler captures availability once, so reduced catalogs keep the same crop contract
without importing canonical content into the supplied catalog.

Route construction prefers Stepping Stones over dry fill for actual Water cells,
when that definition is available in the supplied catalog. Reduced catalogs retain
the existing route surface fallback. Existing mixed crossings need no construction
allowance and retain their terrain. Newly constructed complete-width corridors
still count every changed Water Cell and longest Water run against the existing
limits, preparation costs, reservation permissions and protected Site geometry.
No final repair pass connects all islands. Site construction can replace mixed
Water with dry terrain only with explicit FillWater preparation permission and
charges the existing Water preparation cost.

Terrain analysis retains its land graph and separately reports WaterTravelLabels
and WaterConnectivity, derived from water movement permission with the same
Machine and openable-Edge policy. Mixed cells can join both graphs; neither graph
proves 2×2 access. Semantic Water membership groups water-containing landscape
formations and informs shores, route preferences and dry-rock exclusions. Island
creation remains limited to exact open Water, preserving existing mixed terrain.

`MixedWaterGenerationTests` owns small fixtures covering overlapping sampling,
sparse output, dual connectivity, Walls, route construction limits and protected
cells. Existing Mountain Dungeon, Civilization, Population and Quick Play checks
remain required affected-project verification.
