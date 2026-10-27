# 11 — Catalogue API & données (généré)

> Généré par `tools/index_source.py` depuis le **source original** du jeu.
> Ne pas éditer à la main : relancer l'indexeur après un patch du jeu.

Source : `/home/mio/sts2/game/pck/src/Core` (v0.107.1)

## Volumétrie

- Cartes : **578** — {'Power': 111, 'Skill': 234, 'Attack': 195, 'Curse': 18, 'Status': 17, 'Quest': 3}
- Cartes à payoff conditionnel (glow / historique) : **34**
- Monstres : **123**, dont 103 avec moves offensifs
- Monstres à HP ascensionnels : 100
- Pouvoirs modifiant les dégâts : **30**

## Multiplicateurs de dégâts (pouvoirs)

| Pouvoir | Hooks | Littéraux retournés |
|---|---|---|
| `AccuracyPower` | Additive | [0.0] |
| `CalcifyPower` | Additive | [0.0] |
| `ColossusPower` | Multiplicative | [1.0] |
| `ConquerorPower` | Multiplicative | [1.0, 2.0] |
| `CoveredPower` | Multiplicative | [0.0, 1.0] |
| `DiamondDiademPower` | Multiplicative | [0.5, 1.0] |
| `DoubleDamagePower` | Multiplicative | [1.0, 2.0] |
| `FlankingPower` | Multiplicative | [1.0] |
| `FlutterPower` | Multiplicative | [1.0] |
| `GigantificationPower` | Multiplicative | [1.0, 3.0] |
| `GuardedPower` | Multiplicative | [0.5, 1.0] |
| `HangPower` | Multiplicative | [1.0] |
| `HardToKillPower` | Cap | — |
| `IntangiblePower` | Cap | [1.0] |
| `InterceptPower` | Multiplicative | [1.0] |
| `KnockdownPower` | Multiplicative | [1.0] |
| `LeadershipPower` | Additive | [0.0] |
| `LethalityPower` | Multiplicative | [1.0] |
| `PhantomBladesPower` | Additive | [0.0] |
| `ShrinkPower` | Multiplicative | [1.0] |
| `SlowPower` | Multiplicative | [1.0] |
| `SoarPower` | Multiplicative | [1.0] |
| `StrengthPower` | Additive | [0.0] |
| `SurroundedPower` | Multiplicative | [1.0, 1.5] |
| `TaintedPower` | Additive | [0.0] |
| `TankPower` | Multiplicative | [1.0, 2.0] |
| `TrackingPower` | Multiplicative | [1.0] |
| `VigorPower` | Additive | [0.0] |
| `VulnerablePower` | Multiplicative | [1.0] |
| `WeakPower` | Multiplicative | [1.0] |

## Cartes à payoff conditionnel (priorité de tour)

| Carte | Type | Coût | Rareté | GlowGold | Historique | Fichier |
|---|---|---|---|---|---|---|
| `BansheesCry` | Attack | 9 | Rare | — | ✅ | `Models/Cards/BansheesCry.cs` |
| `BeatIntoShape` | Attack | 1 | Rare | — | ✅ | `Models/Cards/BeatIntoShape.cs` |
| `Bolas` | Attack | 0 | Rare | — | ✅ | `Models/Cards/Bolas.cs` |
| `BubbleBubble` | Skill | 1 | Uncommon | ✅ | — | `Models/Cards/BubbleBubble.cs` |
| `Clash` | Attack | 0 | Event | ✅ | — | `Models/Cards/Clash.cs` |
| `DeathMarch` | Attack | 1 | Uncommon | — | ✅ | `Models/Cards/DeathMarch.cs` |
| `DeathsDoor` | Skill | 1 | Uncommon | ✅ | ✅ | `Models/Cards/DeathsDoor.cs` |
| `Dismantle` | Attack | 1 | Uncommon | ✅ | — | `Models/Cards/Dismantle.cs` |
| `Eidolon` | Skill | 2 | Rare | ✅ | — | `Models/Cards/Eidolon.cs` |
| `EvilEye` | Skill | 1 | Uncommon | ✅ | ✅ | `Models/Cards/EvilEye.cs` |
| `Fetch` | Attack | 0 | Uncommon | ✅ | ✅ | `Models/Cards/Fetch.cs` |
| `Finisher` | Attack | 1 | Uncommon | — | ✅ | `Models/Cards/Finisher.cs` |
| `Flatten` | Attack | 2 | Common | ✅ | ✅ | `Models/Cards/Flatten.cs` |
| `ForgottenRitual` | Skill | 1 | Uncommon | ✅ | ✅ | `Models/Cards/ForgottenRitual.cs` |
| `Ftl` | Attack | 0 | Uncommon | ✅ | ✅ | `Models/Cards/Ftl.cs` |
| `GangUp` | Attack | 1 | Uncommon | — | ✅ | `Models/Cards/GangUp.cs` |
| `GoForTheEyes` | Attack | 0 | Common | ✅ | — | `Models/Cards/GoForTheEyes.cs` |
| `GoldAxe` | Attack | 1 | Rare | — | ✅ | `Models/Cards/GoldAxe.cs` |
| `GrandFinale` | Attack | 0 | Rare | ✅ | — | `Models/Cards/GrandFinale.cs` |
| `HeavenlyDrill` | Attack | 0 | Rare | ✅ | — | `Models/Cards/HeavenlyDrill.cs` |
| `HelixDrill` | Attack | 0 | Rare | — | ✅ | `Models/Cards/HelixDrill.cs` |
| `Impatience` | Skill | 0 | Uncommon | ✅ | — | `Models/Cards/Impatience.cs` |
| `LunarBlast` | Attack | 0 | Uncommon | — | ✅ | `Models/Cards/LunarBlast.cs` |
| `MakeItSo` | Attack | 0 | Rare | — | ✅ | `Models/Cards/MakeItSo.cs` |
| `MementoMori` | Attack | 1 | Uncommon | — | ✅ | `Models/Cards/MementoMori.cs` |
| `Normality` | Curse | ? | Curse | — | ✅ | `Models/Cards/Normality.cs` |
| `PactsEnd` | Attack | 0 | Rare | ✅ | — | `Models/Cards/PactsEnd.cs` |
| `Pinpoint` | Attack | 3 | Uncommon | — | ✅ | `Models/Cards/Pinpoint.cs` |
| `Radiate` | Attack | 0 | Uncommon | — | ✅ | `Models/Cards/Radiate.cs` |
| `Rattle` | Attack | 1 | Uncommon | — | ✅ | `Models/Cards/Rattle.cs` |
| `Restlessness` | Skill | 0 | Uncommon | ✅ | — | `Models/Cards/Restlessness.cs` |
| `Spite` | Attack | 0 | Uncommon | ✅ | ✅ | `Models/Cards/Spite.cs` |
| `Stomp` | Attack | 3 | Uncommon | — | ✅ | `Models/Cards/Stomp.cs` |
| `ThrummingHatchet` | Attack | 1 | Uncommon | — | ✅ | `Models/Cards/ThrummingHatchet.cs` |

## Monstres : moves offensifs résolus

Les valeurs sont `valeurAscension/valeurNormale` quand le jeu scale par ascension.

| Monstre | HP (asc/normal) | Moves offensifs |
|---|---|---|
| `Aeonglass` | 535/512 | EBB_MOVE=SingleAttackIntent(EbbDamage),new DefendIntent()→[?,?]; EYE_LASERS_MOVE=MultiAttackIntent(EyeLasersDamage,EyeLasersRepeat)→[12/11,2] |
| `AssassinRubyRaider` | 19/18 | KILLSHOT_MOVE=SingleAttackIntent(KillshotDamage)→[11/10] |
| `AxeRubyRaider` | 21/20 | SWING_1=SingleAttackIntent(SwingDamage),new DefendIntent()→[?,?]; SWING_2=SingleAttackIntent(SwingDamage),new DefendIntent()→[?,?]; BIG_SWING=SingleAttackIntent(BigSwingDamage)→[13/12] |
| `Axebot` | 76/70 | ONE_TWO_MOVE=MultiAttackIntent(OneTwoDamage,2)→[10/9,2]; HAMMER_UPPERCUT_MOVE=SingleAttackIntent(HammerUppercutDamage),new DebuffIntent()→[?,?] |
| `BowlbugEgg` | 23/21 | BITE_MOVE=SingleAttackIntent(BiteDamage),new DefendIntent()→[?,?] |
| `BowlbugNectar` | 36/35 | THRASH_MOVE=SingleAttackIntent(ThrashDamage)→[3]; THRASH2_MOVE=SingleAttackIntent(ThrashDamage)→[3] |
| `BowlbugRock` | 46/45 | HEADBUTT_MOVE=SingleAttackIntent(HeadbuttDamage)→[16/15] |
| `BowlbugSilk` | 41/40 | THRASH_MOVE=MultiAttackIntent(ThrashDamage,2)→[5/4,2] |
| `BruteRubyRaider` | 31/30 | BEAT_MOVE=SingleAttackIntent(BeatDamage)→[8/7] |
| `BygoneEffigy` | 132/127 | SLASHES_MOVE=SingleAttackIntent(SlashDamage)→[15/13] |
| `Byrdonis` | 90/81 | PECK_MOVE=MultiAttackIntent(PeckDamage,PeckRepeat)→[4/3,3/3]; SWOOP_MOVE=SingleAttackIntent(SwoopDamage)→[19/17] |
| `CalcifiedCultist` | 39/38 | DARK_STRIKE_MOVE=SingleAttackIntent(DarkStrikeDamage)→[11/9] |
| `CeremonialBeast` | 262/252 | PLOW_MOVE=SingleAttackIntent(PlowDamage),new BuffIntent()→[?,?]; STOMP_MOVE=SingleAttackIntent(StompDamage)→[17/15]; CRUSH_MOVE=SingleAttackIntent(CrushDamage),new BuffIntent()→[?,?] |
| `Chomper` | 63/60 | CLAMP_MOVE=MultiAttackIntent(ClampDamage,2)→[9/8,2] |
| `CorpseSlug` | 27/25 | WHIP_SLAP_MOVE=MultiAttackIntent(WhipSlapDamage,WhipSlapRepeat)→[3,2]; GLOMP_MOVE=SingleAttackIntent(GlompDamage)→[9/8] |
| `CrossbowRubyRaider` | 19/18 | FIRE_MOVE=SingleAttackIntent(FireDamage)→[16/14] |
| `Crusher` | 219/209 | THRASH_MOVE=SingleAttackIntent(ThrashDamage)→[14/12]; ENLARGING_STRIKE_MOVE=SingleAttackIntent(EnlargingStrikeDamage)→[4/4]; BUG_STING_MOVE=MultiAttackIntent(BugStingDamage,BugStingTimes),new DebuffIntent()→[7/6,?,?]; GUARDED_STRIKE_MOVE=SingleAttackIntent(GuardedStrikeDamage),new DefendIntent()→[?,?] |
| `CubexConstruct` | 70/65 | REPEATER_BLAST_MOVE=SingleAttackIntent(BlastDamage),new BuffIntent()→[?,?]; REPEATER_BLAST_MOVE_2=SingleAttackIntent(BlastDamage),new BuffIntent()→[?,?]; EXPEL_MOVE=MultiAttackIntent(ExpelDamage,2)→[6/5,2] |
| `DampCultist` | 52/51 | DARK_STRIKE_MOVE=SingleAttackIntent(DarkStrikeDamage)→[3/1] |
| `DecimillipedeSegment` | 46/40 | WRITHE_MOVE=MultiAttackIntent(WritheDamage,2)→[6/5,2]; BULK_MOVE=SingleAttackIntent(BulkDamage),new BuffIntent()→[?,?]; CONSTRICT_MOVE=SingleAttackIntent(ConstrictDamage),new DebuffIntent()→[?,?] |
| `DevotedSculptor` | 172/162 | SAVAGE_MOVE=SingleAttackIntent(SavageDamage)→[15/12] |
| `Entomancer` | 155/145 | BEES_MOVE=MultiAttackIntent(BeesDamage,BeesRepeat)→[3/3,8/7]; SPEAR_MOVE=SingleAttackIntent(SpearMoveDamage)→[20/18] |
| `Exoskeleton` | 25/24 | SKITTER_MOVE=MultiAttackIntent(SkitterDamage,SkitterRepeats)→[1,4/3]; MANDIBLES_MOVE=SingleAttackIntent(MandiblesDamage)→[9/8] |
| `Fabricator` | 155/150 | FABRICATING_STRIKE_MOVE=SingleAttackIntent(FabricatingStrikeDamage),new SummonIntent()→[?,?]; DISINTEGRATE_MOVE=SingleAttackIntent(DisintegrateDamage)→[13/11] |
| `FakeMerchantMonster` | 175/165 | SWIPE_MOVE=SingleAttackIntent(SwipeDamage)→[15/13]; SPEW_COINS_MOVE=MultiAttackIntent(2,8)→[2,8]; THROW_RELIC_MOVE=SingleAttackIntent(ThrowRelicDamage),new DebuffIntent()→[?,?] |
| `FlailKnight` | 108/101 | FLAIL_MOVE=MultiAttackIntent(FlailDamage,2)→[10/9,2]; RAM_MOVE=SingleAttackIntent(RamDamage)→[17/15] |
| `Flyconid` | 51/47 | FRAIL_SPORES_MOVE=SingleAttackIntent(SporeDamage),new DebuffIntent()→[?,?]; SMASH_MOVE=SingleAttackIntent(SmashDamage)→[12/11] |
| `Fogmog` | 78/74 | SWIPE_MOVE=SingleAttackIntent(SwipeDamage),new BuffIntent()→[?,?]; SWIPE_RANDOM_MOVE=SingleAttackIntent(SwipeDamage),new BuffIntent()→[?,?]; HEADBUTT_MOVE=SingleAttackIntent(HeadbuttDamage)→[16/14] |
| `FossilStalker` | 54/51 | TACKLE_MOVE=SingleAttackIntent(TackleDamage),new DebuffIntent()→[?,?]; LATCH_MOVE=SingleAttackIntent(LatchDamage)→[14/12]; LASH_MOVE=MultiAttackIntent(LashDamage,LashRepeat)→[4/3,2] |
| `FrogKnight` | 199/191 | STRIKE_DOWN_EVIL=SingleAttackIntent(StrikeDownEvilDamage)→[23/21]; TONGUE_LASH=SingleAttackIntent(TongueLashDamage),new DebuffIntent()→[?,?]; BEETLE_CHARGE=SingleAttackIntent(BeetleChargeDamage)→[40/35] |
| `FuzzyWurmCrawler` | 58/55 | FIRST_ACID_GOOP=SingleAttackIntent(AcidGoopDamage)→[6/4]; ACID_GOOP=SingleAttackIntent(AcidGoopDamage)→[6/4] |
| `GlobeHead` | 158/148 | THUNDER_STRIKE=MultiAttackIntent(ThunderStrikeDamage,3)→[7/6,3]; SHOCKING_SLAP=SingleAttackIntent(ShockingSlapDamage),new DebuffIntent()→[?,?]; GALVANIC_BURST=SingleAttackIntent(GalvanicBurstDamage),new BuffIntent()→[?,?] |
| `GremlinMerc` | 51/47 | GIMME_MOVE=MultiAttackIntent(GimmeDamage,GimmeRepeat)→[8/7,2]; DOUBLE_SMASH_MOVE=MultiAttackIntent(DoubleSmashDamage,DoubleSmashRepeat),new DebuffIntent()→[7/6,?,?]; HEHE_MOVE=SingleAttackIntent(HeheDamage),new BuffIntent()→[?,?] |
| `HauntedShip` | 67/63 | SWIPE_MOVE=SingleAttackIntent(SwipeDamage)→[14/13]; STOMP_MOVE=MultiAttackIntent(StompDamage,StompRepeat)→[5/4,3] |
| `HunterKiller` | 126/121 | BITE_MOVE=SingleAttackIntent(BiteDamage)→[19/17]; PUNCTURE_MOVE=MultiAttackIntent(PunctureDamage,3)→[8/7,3] |
| `InfestedPrism` | 171/161 | JAB_MOVE=SingleAttackIntent(JabDamage)→[17/15]; RADIATE_MOVE=SingleAttackIntent(RadiateDamage),new DefendIntent()→[?,?]; WHIRLWIND_MOVE=MultiAttackIntent(WhirlwindDamage,WhirlwindRepeat)→[6/5,3]; PULSATE_MOVE=SingleAttackIntent(PulsateDamage),new BuffIntent(),new DefendIntent()→[?,?,?] |
| `Inklet` | 12/11 | JAB_MOVE=SingleAttackIntent(JabDamage)→[4/3]; WHIRLWIND_MOVE=MultiAttackIntent(WhirlwindDamage,3)→[3/2,3]; PIERCING_GAZE_MOVE=SingleAttackIntent(PiercingGazeDamage)→[11/10] |
| `KinFollower` | 62/58 | QUICK_SLASH_MOVE=SingleAttackIntent(QuickSlashDamage)→[5/5]; BOOMERANG_MOVE=MultiAttackIntent(BoomerangDamage,2)→[2/2,2] |
| `KinPriest` | 199/190 | ORB_OF_FRAILTY_MOVE=SingleAttackIntent(OrbOfFrailtyDamage),new DebuffIntent()→[?,?]; ORB_OF_WEAKNESS_MOVE=SingleAttackIntent(OrbOfWeaknessDamage),new DebuffIntent()→[?,?]; BEAM_MOVE=MultiAttackIntent(BeamDamage,3)→[3/3,3] |
| `KnowledgeDemon` | 399/379 | SLAP_MOVE=SingleAttackIntent(SlapDamage)→[18/17]; KNOWLEDGE_OVERWHELMING_MOVE=MultiAttackIntent(KnowledgeOverwhelmingDamage,3)→[9/8,3]; PONDER_MOVE=SingleAttackIntent(PonderDamage),new HealIntent(),new BuffIntent()→[?,?,?] |
| `LagavulinMatriarch` | 233/222 | SLASH_MOVE=SingleAttackIntent(SlashDamage)→[21/19]; SLASH2_MOVE=SingleAttackIntent(Slash2Damage),new DefendIntent()→[2,?]; DISEMBOWEL_MOVE=MultiAttackIntent(DisembowelDamage,DisembowelRepeat)→[10/9,2] |
| `LeafSlimeM` | 33/32 | CLUMP_SHOT=SingleAttackIntent(ClumpDamage)→[9/8] |
| `LeafSlimeS` | 12/11 | TACKLE_MOVE=SingleAttackIntent(TackleDamage)→[4/3] |
| `LivingFog` | 82/80 | ADVANCED_GAS_MOVE=SingleAttackIntent(AdvancedGasDamage),new CardDebuffIntent()→[?,?]; BLOAT_MOVE=SingleAttackIntent(BloatDamage),new SummonIntent()→[?,?]; SUPER_GAS_BLAST_MOVE=SingleAttackIntent(SuperGasBlastDamage)→[9/8] |
| `LivingShield` | 65/55 | SHIELD_SLAM_MOVE=SingleAttackIntent(ShieldSlamDamage)→[6]; SMASH_MOVE=SingleAttackIntent(SmashDamage),new BuffIntent()→[?,?] |
| `LouseProgenitor` | 138/134 | WEB_CANNON_MOVE=SingleAttackIntent(WebDamage),new DebuffIntent()→[?,?]; POUNCE_MOVE=SingleAttackIntent(PounceDamage)→[16/14] |
| `MagiKnight` | 89/82 | POWER_SHIELD_MOVE=SingleAttackIntent(PowerShieldDamage),new DefendIntent()→[?,?]; MAGIC_BOMB=SingleAttackIntent(BombDamage)→[40/35]; RAM_MOVE=SingleAttackIntent(SpearDamage)→[11/10] |
| `Mawler` | 76/72 | RIP_AND_TEAR_MOVE=SingleAttackIntent(RipAndTearDamage)→[16/14]; CLAW_MOVE=MultiAttackIntent(ClawDamage,2)→[5/4,2] |
| `MechaKnight` | 320/300 | CHARGE_MOVE=SingleAttackIntent(ChargeDamage)→[30/25]; HEAVY_CLEAVE_MOVE=SingleAttackIntent(HeavyCleaveDamage)→[40/35] |
| `MockAttackAndSummonMinionMonster` | 10 | ATTACK_AND_SUMMON_MINION=SingleAttackIntent(1)→[1] |
| `MockAttackMonster` | 9999 | ATTACK=SingleAttackIntent(1)→[1] |
| `MultiAttackMoveMonster` | 999 | POKE=MultiAttackIntent(1,5)→[1,5] |
| `Myte` | 64/61 | BITE_MOVE=SingleAttackIntent(BiteDamage)→[15/13]; SUCK_MOVE=SingleAttackIntent(SuckDamage),new BuffIntent()→[?,?] |
| `Nibbit` | 44/42 | BUTT_MOVE=SingleAttackIntent(ButtDamage)→[13/12]; SLICE_MOVE=SingleAttackIntent(SliceDamage),new DefendIntent()→[?,?] |
| `Ovicopter` | 126/124 | SMASH_MOVE=SingleAttackIntent(SmashDamage)→[17/16]; TENDERIZER_MOVE=SingleAttackIntent(TenderizerDamage),new DebuffIntent()→[?,?] |
| `OwlMagistrate` | 247/231 | MAGISTRATE_SCRUTINY=SingleAttackIntent(ScrutinyDamage)→[17/16]; PECK_ASSAULT=MultiAttackIntent(PeckAssaultDamage,6)→[4/4,6]; VERDICT=SingleAttackIntent(VerdictDamage),new DebuffIntent()→[?,?] |
| `Parafright` | 21 | SLAM_MOVE=SingleAttackIntent(SlamDamage)→[17/16] |
| `PhantasmalGardener` | 27/26 | BITE_MOVE=SingleAttackIntent(BiteDamage)→[5/5]; LASH_MOVE=SingleAttackIntent(LashDamage)→[7/7]; FLAIL_MOVE=MultiAttackIntent(FlailDamage,FlailRepeat)→[1,3/3] |
| `PhrogParasite` | 66/61 | LASH_MOVE=MultiAttackIntent(LashDamage,4)→[5/4,4] |
| `PunchConstruct` | 60/55 | STRONG_PUNCH_MOVE=SingleAttackIntent(StrongPunchDamage)→[16/14]; FAST_PUNCH_MOVE=MultiAttackIntent(FastPunchDamage,FastPunchRepeat),new DebuffIntent()→[6/5,?,?] |
| `Queen` | 419/400 | OFF_WITH_YOUR_HEAD_MOVE=MultiAttackIntent(OffWithYourHeadDamage,5)→[4/3,5]; EXECUTION_MOVE=SingleAttackIntent(ExecutionDamage)→[18/15] |
| `Rocket` | 209/199 | TARGETING_RETICLE_MOVE=SingleAttackIntent(TargetingReticleDamage)→[4/3]; PRECISION_BEAM_MOVE=SingleAttackIntent(PrecisionBeamDamage)→[20/18]; LASER_MOVE=SingleAttackIntent(LaserDamage)→[35/31] |
| `ScrollOfBiting` | 33/30 | CHOMP=SingleAttackIntent(ChompDamage)→[16/14]; CHEW=MultiAttackIntent(ChewDamage,2)→[6/5,2] |
| `Seapunk` | 47/44 | SEA_KICK_MOVE=SingleAttackIntent(SeaKickDamage)→[13/11]; SPINNING_KICK_MOVE=MultiAttackIntent(SpinningKickDamage,SpinningKickRepeat)→[2,4] |
| `SewerClam` | 58/56 | JET_MOVE=SingleAttackIntent(JetDamage)→[11/10] |
| `ShrinkerBeetle` | 40/38 | CHOMP_MOVE=SingleAttackIntent(ChompDamage)→[8/7]; STOMP_MOVE=SingleAttackIntent(StompDamage)→[14/13] |
| `SingleAttackMoveMonster` | 999 | POKE=SingleAttackIntent(1)→[1] |
| `SkulkingColony` | 80/75 | ZOOM_MOVE=SingleAttackIntent(ZoomDamage)→[16/14]; ZOOM_MOVE_2=SingleAttackIntent(ZoomDamage)→[16/14]; INERTIA_MOVE=SingleAttackIntent(InertiaDamage),new BuffIntent()→[?,?]; PIERCING_STABS_MOVE=MultiAttackIntent(PiercingStabsDamage,PiercingStabsRepeat)→[8/7,2] |
| `SlimedBerserker` | 281/261 | SMOTHER_MOVE=SingleAttackIntent(SmotherDamage)→[33/30]; FURIOUS_PUMMELING_MOVE=MultiAttackIntent(PummelingDamage,4)→[5/4,4] |
| `SlitheringStrangler` | 54/53 | THWACK=SingleAttackIntent(ThwackDamage),new DefendIntent()→[?,?]; LASH=SingleAttackIntent(LashDamage)→[13/12] |
| `SludgeSpinner` | 41/37 | OIL_SPRAY_MOVE=SingleAttackIntent(OilSprayDamage),new DebuffIntent()→[?,?]; SLAM_MOVE=SingleAttackIntent(SlamDamage)→[12/11]; RAGE_MOVE=SingleAttackIntent(RageDamage),new BuffIntent()→[?,?] |
| `SlumberingBeetle` | 89/86 | ROLL_OUT_MOVE=SingleAttackIntent(RolloutDamage),new BuffIntent()→[?,?] |
| `SnappingJaxfruit` | 34/31 | ENERGY_ORB_MOVE=SingleAttackIntent(EnergyDamage),new BuffIntent()→[?,?] |
| `SneakyGremlin` | 11/10 | TACKLE_MOVE=SingleAttackIntent(TackleDamage)→[10/9] |
| `SoulFysh` | 221/211 | DE_GAS_MOVE=SingleAttackIntent(DeGasDamage)→[17/16]; GAZE_MOVE=SingleAttackIntent(GazeDamage),new StatusIntent(GazeMoveAmount)→[?,?]; SCREAM_MOVE=SingleAttackIntent(ScreamDamage),new DebuffIntent()→[?,?] |
| `SoulNexus` | 254/234 | SOUL_BURN_MOVE=SingleAttackIntent(SoulBurnDamage)→[31/29]; MAELSTROM_MOVE=MultiAttackIntent(MaelstromDamage,MaelstromRepeat)→[7/6,4/4]; DRAIN_LIFE_MOVE=SingleAttackIntent(DrainLifeDamage),new DebuffIntent(strong: true)→[?,?] |
| `SpectralKnight` | 97/93 | SOUL_SLASH=SingleAttackIntent(SoulSlashDamage)→[17/15]; SOUL_FLAME=MultiAttackIntent(SoulFlameDamage,3)→[4/3,3] |
| `SpinyToad` | 121/116 | SPIKE_EXPLOSION_MOVE=SingleAttackIntent(ExplosionDamage)→[25/23]; TONGUE_LASH_MOVE=SingleAttackIntent(LashDamage)→[19/17] |
| `Stabbot` | 19/18 | STAB_MOVE=SingleAttackIntent(StabDamage),new DebuffIntent()→[?,?] |
| `TerrorEel` | 150/140 | CRASH_MOVE=SingleAttackIntent(CrashDamage)→[18/16]; THRASH_MOVE=MultiAttackIntent(ThrashDamage,ThrashRepeat),new BuffIntent()→[4/3,?,?] |
| `TestSubject` | ? | BITE_MOVE=SingleAttackIntent(BiteDamage)→[22/20]; SKULL_BASH_MOVE=SingleAttackIntent(SkullBashDamage),new DebuffIntent()→[?,?]; MULTI_CLAW_MOVE=MultiAttackIntent(MultiClawDamage,() => MultiClawTotalCount)→[11/10,?]; PHASE3_LACERATE_MOVE=MultiAttackIntent(Phase3LacerateDamage,3)→[11/10,3] |
| `TheAdversaryMkOne` | 100 | SMASH_MOVE=SingleAttackIntent(SmashDamage)→[12]; BEAM_MOVE=SingleAttackIntent(BeamDamage)→[15]; BARRAGE_MOVE=MultiAttackIntent(BarrageDamage,BarrageRepeat),new BuffIntent()→[8,?,?] |
| `TheAdversaryMkThree` | 300/300 | CRASH_MOVE=SingleAttackIntent(CrashDamage)→[15]; FLAME_BEAM_MOVE=SingleAttackIntent(FlameBeamDamage)→[18]; BARRAGE_MOVE=MultiAttackIntent(BarrageDamage,BarrageRepeat),new BuffIntent()→[10,?,?] |
| `TheAdversaryMkTwo` | 200/200 | BASH_MOVE=SingleAttackIntent(BashDamage)→[13]; FLAME_BEAM_MOVE=SingleAttackIntent(FlameBeamDamage)→[16]; BARRAGE_MOVE=MultiAttackIntent(BarrageDamage,BarrageRepeat),new BuffIntent()→[9,?,?] |
| `TheForgotten` | 111/106 | DREAD=SingleAttackIntent(() => DreadDamage)→[?] |
| `TheInsatiable` | 341/321 | THRASH_MOVE=MultiAttackIntent(ThrashDamage,2)→[9/8,2]; THRASH_MOVE_2=MultiAttackIntent(ThrashDamage,2)→[9/8,2]; LUNGING_BITE_MOVE=SingleAttackIntent(BiteDamage)→[31/28] |
| `TheLost` | 99/93 | EYE_LASERS=MultiAttackIntent(EyeLasersDamage,2)→[5/4,2] |
| `TheObscura` | 129/123 | PIERCING_GAZE_MOVE=SingleAttackIntent(PiercingGazeDamage)→[11/10]; HARDENING_STRIKE_MOVE=SingleAttackIntent(HardeningStrikeDamage),new DefendIntent()→[?,?] |
| `ThievingHopper` | 84/79 | THIEVERY_MOVE=SingleAttackIntent(TheftDamage),new CardDebuffIntent()→[?,?]; NAB_MOVE=SingleAttackIntent(NabDamage)→[16/14]; HAT_TRICK_MOVE=SingleAttackIntent(HatTrickDamage)→[23/21] |
| `Toadpole` | 22/21 | SPIKE_SPIT_MOVE=MultiAttackIntent(SpikeSpitDamage,SpikeSpitRepeat)→[4/3,3]; WHIRL_MOVE=SingleAttackIntent(WhirlDamage)→[8/7] |
| `TorchHeadAmalgam` | 211/199 | TACKLE_MOVE=SingleAttackIntent(TackleDamage)→[19/18]; TACKLE_2_MOVE=SingleAttackIntent(TackleDamage)→[19/18]; BEAM_MOVE=MultiAttackIntent(SoulBeamDamage,3)→[8/8,3]; TACKLE_3_MOVE=SingleAttackIntent(WeakTackleDamage)→[15/14] |
| `ToughEgg` | 15/14 | NIBBLE_MOVE=SingleAttackIntent(NibbleDamage)→[5/4] |
| `TrackerRubyRaider` | 22/21 | HOUNDS_MOVE=MultiAttackIntent(HoundsDamage,HoundsRepeat)→[1/1,9/8] |
| `Tunneler` | 92/87 | BITE_MOVE=SingleAttackIntent(BiteDamage)→[15/13]; BELOW_MOVE=SingleAttackIntent(BelowDamage)→[26/23] |
| `TurretOperator` | 51/41 | UNLOAD_MOVE=MultiAttackIntent(FireDamage,5)→[4/3,5]; UNLOAD_MOVE_2=MultiAttackIntent(FireDamage,5)→[4/3,5] |
| `TwigSlimeM` | 27/26 | POKEY_POUNCE_MOVE=SingleAttackIntent(ClumpDamage)→[12/11] |
| `TwigSlimeS` | 8/7 | TACKLE_MOVE=SingleAttackIntent(TackleDamage)→[5/4] |
| `TwoTailedRat` | 18/17 | SCRATCH_MOVE=SingleAttackIntent(ScratchDamage)→[9/8]; DISEASE_BITE_MOVE=SingleAttackIntent(DiseaseBiteDamage)→[7/6] |
| `Vantom` | 183/173 | INK_BLOT_MOVE=SingleAttackIntent(InkBlotDamage)→[8/7]; INKY_LANCE_MOVE=MultiAttackIntent(InkyLanceDamage,2)→[7/6,2]; DISMEMBER_MOVE=SingleAttackIntent(DismemberDamage),new StatusIntent(3)→[?,3] |
| `VineShambler` | 64/61 | GRASPING_VINES_MOVE=SingleAttackIntent(GraspingVinesDamage),new CardDebuffIntent()→[?,?]; SWIPE_MOVE=MultiAttackIntent(SwipeDamage,2)→[7/6,2]; CHOMP_MOVE=SingleAttackIntent(ChompDamage)→[18/16] |
| `WaterfallGiant` | 250/240 | STOMP_MOVE=SingleAttackIntent(StompDamage),new DebuffIntent(),new BuffIntent()→[?,?,?]; RAM_MOVE=SingleAttackIntent(RamDamage),new BuffIntent()→[?,?]; PRESSURE_GUN_MOVE=SingleAttackIntent(() => CurrentPressureGunDamage),new BuffIntent()→[?,?]; PRESSURE_UP_MOVE=SingleAttackIntent(PressureUpDamage),new BuffIntent()→[?,?] |
| `Wriggler` | 18/17 | NASTY_BITE_MOVE=SingleAttackIntent(BiteDamage)→[7/6] |
| `Zapbot` | 19/18 | ZAP=SingleAttackIntent(ZapDamage)→[15/14] |

