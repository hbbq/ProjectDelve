using ProjectDelve.Engine;
namespace ProjectDelve.Web;

internal static class GenombrottetScenario
{
    // Original authored map and placements, retained in the scenario-import format.
    //internal const string Export = "DELVE1:H4sIAAAAAAAACtWZQW_aMBTH75X6HSKfiwQJIYVbtWrrbtPKxGHiYGIvWJgYGUeMVv3uk4EQu6EdcRJsTg3Paf6_2H7_95K83t54HpgxyBEYea_yl-eBDUFiDkZeL7w7ROaYJHMhQ908hPAfmFExxpxDkoKRB54FS_FXyhgH-UkxpnQNRt7v_W8vl9gNrtiaCMLk_yphzwN_pVD_TottwcjrFpE3ZRSIgmECBeYgHyvOqqXbs6TrW9INLqEbWLrfwNL6Bpb2s29Jt-dO_ob_1_3G4XrdtO7Akm5kSffeku7wArqh0fIWJXFCxHwMZxQ3nVT3F6cYGFWMpqcisJTngaU8DyzleWApz4Mm8nx_MM3bUYwSbNiOdss4_Q9wEOE4PlwJPLJNCtTRBUllrw0mkNI6EzR0DejeNaDINaCBa0Cha0B914AqVBkN6OfueflCRAPniKLrJGptG_muATlXznpXkPmWZ8hvmugLZWuMHuULuzpcvStojYZ2DWl4fhNtjyhyjshyYTvV9IeutbSW25HIuVWLnNvZoXN-FDrnR6bF7aJEgXNEfj2iZjoA__xPKvZmqmu5lrjXT7b8sL0_mMo_-2uBLCVivF3h70h5CQcSNqMk7YjtCh-_Gyc8S4UeemHLGcF6THBGqR5aLzDFgqUdyOM55vrgDPIZ5AS-U9uQF8iRHuMsyQ5qMjIt7kBhP06qcmtyIsr4koygw7A86vjKEEzjOeP6KpV6Ir1uFMsjV1lkHMsr_1rx3Q7ej-XntI6pvvrTm5ImME-svCGn9qii-5RjoKpb6KWnCdBy3phy-q1O6GfJbEgctQp82mDKqL1zULutop4wPUPOdpNKMeKGMf1GvbRcxQx3qPqVQC_KZ3Eq1QomOI23Z5SrquwxS3cWgiU_eMgEW0KBUUtlp55aZVOuJ1fVWuupmRtkPV0Tn9MVn7IlTNuyKnOpqm7ziZKSiIwjzFvoG_PG-4lR9CPv9VtKi1zrQQgYL5QBAXmCxTsjy89-Jki5_qm5PJpbO_nlCLZ5oi7M17iyr19qsnapcXvz9g_RyAgrfywAAA";
    internal const string Export = "DELVE1:H4sIAAAAAAAACt2aTW-bMBjH75X6HZDPjRQIoSS3adXW3aat0w5TDgZ7iVWCI2OUpVW_--RQEhNoF97ih51KHrv8f357XoDn6yvLQgHHgqC59ax-WRbaMiJXaG7Z05tXy4qy5Uoqk5ObCP2N00g-UCEwi9HcQt8lj-mniHOB8k4hjaIEza1f2W8rl9g3bnjCJOPqfzWzZaE_SmhyU7Dt0NwaHy0vWiuSR4bPAicJytuOvVrp2oZ0HUO6E0O6riHdqSFd7xK6LhzdqSFd15DuxJCuY0jXNqRbPy5kF4s8TFGypM3C1LhM479BQ5ig4euN0B3fxkhvfWSxCsHoJ46iVvMDDciBBjSBBuQOAGh2DtC3fZJ4ISJ73A7pY8QTSu5UttoxmG12rmbDQBqDQzK8x214sQRcMDmUn50RdeMGqrJ7wzMFLqrUKOgvdOJqlPrm4pxrdNGm0IA8aEC3_8sM9bervfNL4wsR-dAWbTYAIHgZmw0vY3PBZWwDJepx2Wxw_qgKyQGXHJndSQ40oPH5T9aNPfvzoC2ZB6Baq_EqxtgDNw9a2m8WyIMGdAsNyIcGNAPnHuE57M49dkePsxxwMwXOZ9d5sW7swPnQXJIPzWn70MKa4bcQHtQ3bVVgput_eKUtvMq258I2u1ioP9nNUBoz-bDb0C9E-6QDBVgEWDAcj-RuQw8fKAq-TGnRtGVPWJCi7YmvA3bSbynSWBZNySONqOTxCItwRcVJfx5E7EReCh5FRVPA18GIrTeZVRkXx3FpIzpMtTZgNT3VA1VwjLx2UVcjW2vCcbjiorh-pRK3uL-PK6fWX6aCqjv_2Ij9umVteZ-3UMuT3xXmpEvMig3RFafTJWfFJi1zOudw6snWSQiABeoPBbSQUfdAWnZGDUHdXrdof5idOqb3_HhDYD31LH4M28m8lmNLQ85Zv_u0HPAacvZ8nopRuOXZtwfE2tb1awkLXtI43PWTsYQ83m8lqgaB7tM1jntKOpor1c4bmkvVDlRFqQ-p5GssKenJjbdTa-6NW46yrlNtJ1fXNbZTC-o7jX8IaiefC0LFGbVK3X2Ul4D3PCJf86pTl7--evkLBMKEhNM2AAA";
    internal static ScenarioDefinition Definition() => ScenarioDefinitionTransport.Decode(Export) with
    {
        Objectives =
        [
            new("side-1", ScenarioObjective.ReachArea)
            {
                Count = 0, Selectors = [UnitSelector.SideAtStart("side-1")], Area = new(13, 0, 2, 7)
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
            new(UnitTypeIds.SkeletonArcher, "side-2", OrderKind.Attack, Target: OrderTarget.ForSide("side-1")),
            new(UnitTypeIds.BombImp, "side-2", OrderKind.Attack, Target: OrderTarget.ForSide("side-1")),
            new(UnitTypeIds.Grunt, "side-2", OrderKind.HoldPosition)
        ]
    };
}