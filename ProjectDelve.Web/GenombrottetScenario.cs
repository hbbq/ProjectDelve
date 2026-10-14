using ProjectDelve.Engine;
namespace ProjectDelve.Web;

internal static class GenombrottetScenario
{
    // Original authored map and placements, retained in the scenario-import format.
    internal const string Export = "DELVE1:H4sIAAAAAAAACtWZQW_aMBTH75X6HSKfiwQJIYVbtWrrbtPKxGHiYGIvWJgYGUeMVv3uk4EQu6EdcRJsTg3Paf6_2H7_95K83t54HpgxyBEYea_yl-eBDUFiDkZeL7w7ROaYJHMhQ908hPAfmFExxpxDkoKRB54FS_FXyhgH-UkxpnQNRt7v_W8vl9gNrtiaCMLk_yphzwN_pVD_TottwcjrFpE3ZRSIgmECBeYgHyvOqqXbs6TrW9INLqEbWLrfwNL6Bpb2s29Jt-dO_ob_1_3G4XrdtO7Akm5kSffeku7wArqh0fIWJXFCxHwMZxQ3nVT3F6cYGFWMpqcisJTngaU8DyzleWApz4Mm8nx_MM3bUYwSbNiOdss4_Q9wEOE4PlwJPLJNCtTRBUllrw0mkNI6EzR0DejeNaDINaCBa0Cha0B914AqVBkN6OfueflCRAPniKLrJGptG_muATlXznpXkPmWZ8hvmugLZWuMHuULuzpcvStojYZ2DWl4fhNtjyhyjshyYTvV9IeutbSW25HIuVWLnNvZoXN-FDrnR6bF7aJEgXNEfj2iZjoA__xPKvZmqmu5lrjXT7b8sL0_mMo_-2uBLCVivF3h70h5CQcSNqMk7YjtCh-_Gyc8S4UeemHLGcF6THBGqR5aLzDFgqUdyOM55vrgDPIZ5AS-U9uQF8iRHuMsyQ5qMjIt7kBhP06qcmtyIsr4koygw7A86vjKEEzjOeP6KpV6Ir1uFMsjV1lkHMsr_1rx3Q7ej-XntI6pvvrTm5ImME-svCGn9qii-5RjoKpb6KWnCdBy3phy-q1O6GfJbEgctQp82mDKqL1zULutop4wPUPOdpNKMeKGMf1GvbRcxQx3qPqVQC_KZ3Eq1QomOI23Z5SrquwxS3cWgiU_eMgEW0KBUUtlp55aZVOuJ1fVWuupmRtkPV0Tn9MVn7IlTNuyKnOpqm7ziZKSiIwjzFvoG_PG-4lR9CPv9VtKi1zrQQgYL5QBAXmCxTsjy89-Jki5_qm5PJpbO_nlCLZ5oi7M17iyr19qsnapcXvz9g_RyAgrfywAAA";
    internal static ScenarioDefinition Definition() => ScenarioDefinitionTransport.Decode(Export) with
    {
        Objectives =
        [
            new("side-1", ScenarioObjective.ReachArea)
            {
                Count = 0, Selectors = [UnitSelector.SideAtStart("side-1")], Area = new(13, 5, 2, 5)
            },
            new("side-2", ScenarioObjective.DefeatUnits)
            {
                Count = 1, Selectors = [UnitSelector.SideAtStart("side-1")]
            }
        ],
        Orders =
        [
            new(UnitTypeIds.Zombie, "side-2", OrderKind.Attack, Target: OrderTarget.ForSide("side-1")),
            new(UnitTypeIds.Troll, "side-2", OrderKind.Attack, Target: OrderTarget.ForSide("side-1")),
            new(UnitTypeIds.SkeletonArcher, "side-2", OrderKind.HoldPosition),
            new(UnitTypeIds.Grunt, "side-2", OrderKind.HoldPosition)
        ]
    };
}