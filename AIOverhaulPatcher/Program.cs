using System;
using System.Collections.Generic;
using System.Linq;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using AIOverhaulPatcher.Utilities;
using AIOverhaulPatcher.Settings;
using Noggog;
using System.Threading.Tasks;
using Mutagen.Bethesda.Plugins; 

namespace AIOverhaulPatcher
{
    public class Program
    {
        private static Lazy<Settings.Settings> _settings = null!;
        const string AioPatchName = "AIOPatch.esp";
        public static Task<int> Main(string[] args)
        {
            
            return SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch, new PatcherPreferences()
                {
                    ExclusionMods = new List<ModKey>()
                    {
                         new ModKey(AioPatchName, ModType.Plugin),
                         new ModKey("Nemesis PCEA.esp", ModType.Plugin)
                    }
                } )
                .SetAutogeneratedSettings("settings", "settings.json", out _settings)
                .SetTypicalOpen(GameRelease.SkyrimSE, AioPatchName)
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {

            var AIOverhaul = state.LoadOrder.GetModByFileName("AI Overhaul.esp");
            var USLEEP = state.LoadOrder.GetModByFileName("Unofficial Skyrim Special Edition Patch.esp");
            if (USLEEP != null) System.Console.WriteLine("Unofficial Skyrim Special Edition Patch.esp");
            var UsleepOrder = state.LoadOrder.GetFileOrder("Unofficial Skyrim Special Edition Patch.esp");
            System.Console.WriteLine("at " + UsleepOrder);


            if (AIOverhaul == null)
            {
                System.Console.WriteLine("AIOverhaul.esp not found");
                return;
            }


            int bmax = 10;
            int b = 0;
            int processed = 0;
            int total = AIOverhaul.Npcs.Count;

            var AIOFormIDs = AIOverhaul.Npcs.Select(x => x.FormKey).ToList();
            var winningOverrides = state.LoadOrder.PriorityOrder.WinningOverrides<INpcGetter>().Where(x => AIOFormIDs.Contains(x.FormKey)).ToList();
            //var USLEEPandPrior = state.LoadOrder.PriorityOrder.Reverse().Take(UsleepOrder + 1).Select(x => x.Mod).ToList();
            var masterfilenames = AIOverhaul.MasterReferences.Select(x => x.Master.FileName).ToList();
            var MasterFiles = state.LoadOrder.PriorityOrder.Reverse().Where(x => masterfilenames.Contains(x.ModKey.FileName)).ToList();
            var NPCMasters = MasterFiles.Select(x => x.Mod).NotNull().SelectMany(x => x.Npcs).Where(x => AIOFormIDs.Contains(x.FormKey)).ToList();

            var allOverrides = state.LoadOrder.PriorityOrder.Reverse().Skip(UsleepOrder + 1).Select(x => x.Mod).NotNull().SelectMany(x => x.Npcs).Where(x => AIOFormIDs.Contains(x.FormKey)).ToList();


            System.Console.WriteLine(processed + "/" + total + " Npcs");
            foreach (var npc in AIOverhaul.Npcs)
            {
                if (_settings.Value.IgnorePlayerRecord &&  new List<uint>() { 7, 14 }.Contains(npc.FormKey.ID)) continue;

                if (b >= bmax)
                {
                    b = 0;
                    System.Console.WriteLine(processed + "/" + total + " Npcs");
                }

                var winningOverride = winningOverrides.Where(x => x.FormKey == npc.FormKey).First();
                var Masters = NPCMasters.Where(x => x.FormKey == npc.FormKey).ToList();
                var winningMaster = Masters.FirstOrDefault();
                if (winningMaster == null) winningMaster = state.LoadOrder.PriorityOrder.Select(x => x.Mod).NotNull().SelectMany(x => x.Npcs).Where(x => x.FormKey == npc.FormKey).First();
                var overrides = allOverrides.Where(x => x.FormKey == npc.FormKey).ToList();



                bool change = false;

                var patchNpc = state.PatchMod.Npcs.GetOrAddAsOverride(winningOverride);

                if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Protected))
                {
                    patchNpc.Configuration.Flags = patchNpc.Configuration.Flags.SetFlag(NpcConfiguration.Flag.Protected, true);
                    change = true;
                }

                foreach (var fac in npc.Factions)
                    if (!patchNpc.Factions.Select(x => new KeyValuePair<FormKey, int>(x.Faction.FormKey, x.Rank)).Contains(new KeyValuePair<FormKey, int>(fac.Faction.FormKey, fac.Rank)))
                    {
                        patchNpc.Factions.Add(fac.DeepCopy());
                        change = true;

                    }

                if (!patchNpc.Packages.All(x => npc.Packages.Contains(x)) || !npc.Packages.All(x => patchNpc.Packages.Contains(x)))
                {
                    change = true;


                    var PackagesToRemove = Masters.SelectMany(x => x.Packages).Select(x => x.FormKey).Where(x => !npc.Packages.Select(x => x.FormKey).Contains(x)).ToHashSet<FormKey>();

                    var PackagesToAdd = overrides.SelectMany(x => x.Packages).Select(x => x.FormKey).Where(x => !PackagesToRemove.Contains(x)).Distinct().ToList();

                    if (PackagesToAdd.Count > 0 || PackagesToRemove.Count > 0)
                    {
                        var aioOrder = npc.Packages.Select(x => x.FormKey).ToList();
                        patchNpc.Packages.Clear();
                        PackagesToAdd.OrderBy(x => npc.Packages.Contains(x) ? aioOrder.IndexOf(x)  : (npc.Packages.Count + PackagesToAdd.IndexOf(x))).ForEach(x => patchNpc.Packages.Add(x));
                        
                    } 
                }




                var OverwrittenOutfits = Masters.Select(x => x.DefaultOutfit).Select(x => x.FormKey).Distinct().ToHashSet<FormKey>();
                var OverwrittenSleepingOutfit = Masters.Select(x => x.SleepingOutfit).Select(x => x.FormKey).Distinct().ToHashSet<FormKey>();

                FormKey? OverwrittingOutfit = overrides.Select(x => x.DefaultOutfit).Select(x => x.FormKey).Where(x => !x.IsNull && !OverwrittenOutfits.Contains(x)).Prepend(npc.DefaultOutfit.FormKey).LastOrDefault();
                FormKey? OverwrittingSleepingOutfit = overrides.Select(x => x.SleepingOutfit).Select(x => x.FormKey).Where(x => !x.IsNull && !OverwrittenSleepingOutfit.Contains(x)).Prepend(npc.SleepingOutfit.FormKey).LastOrDefault();

                if (npc.DefaultOutfit.FormKey != patchNpc.DefaultOutfit.FormKey)
                {
                    if (OverwrittingOutfit.HasValue && !OverwrittingOutfit.Value.IsNull)
                        patchNpc.DefaultOutfit.SetTo(OverwrittingOutfit);
                    else
                        patchNpc.DefaultOutfit.SetToNull();

                    change = true;

                }

                if (npc.SleepingOutfit.FormKey != patchNpc.SleepingOutfit.FormKey)
                {
                    if (OverwrittingSleepingOutfit.HasValue && !OverwrittingSleepingOutfit.Value.IsNull)
                        patchNpc.SleepingOutfit.SetTo(OverwrittingSleepingOutfit);
                    else
                        patchNpc.SleepingOutfit.SetToNull();
                    change = true;


                }
                if (npc.SpectatorOverridePackageList.FormKey != patchNpc.SpectatorOverridePackageList.FormKey)
                {
                    if (!npc.SpectatorOverridePackageList.IsNull)
                        patchNpc.SpectatorOverridePackageList.SetTo(npc.SpectatorOverridePackageList);
                    else
                        patchNpc.SpectatorOverridePackageList.SetToNull();
                    change = true;

                }

                if (npc.CombatOverridePackageList.FormKey != patchNpc.CombatOverridePackageList.FormKey)
                {
                    if (!npc.CombatOverridePackageList.IsNull)
                        patchNpc.CombatOverridePackageList.SetTo(npc.CombatOverridePackageList);
                    else
                        patchNpc.CombatOverridePackageList.SetToNull();
                    change = true;


                }

                if (npc.AIData.Confidence != patchNpc.AIData.Confidence)
                {
                    // vl: patchNpc.AIData.Confidence = (Confidence)Math.Min((int)patchNpc.AIData.Confidence, (int)npc.AIData.Confidence);
                    patchNpc.AIData.Confidence = npc.AIData.Confidence;
                    change = true;

                }


                if (npc.VirtualMachineAdapter != null)
                {
                    List<IScriptEntryGetter> ScriptsToForward = npc.VirtualMachineAdapter.Scripts.Where(x => patchNpc.VirtualMachineAdapter == null || !patchNpc.VirtualMachineAdapter.Scripts.Select(x => x.Name).Contains(x.Name)).ToList();
                    if (ScriptsToForward.Count > 0)
                    {
                        change = true;


                        if (patchNpc.VirtualMachineAdapter == null)
                            patchNpc.VirtualMachineAdapter = npc.VirtualMachineAdapter.DeepCopy();
                        else
                        {
                            ScriptsToForward.ForEach(x => patchNpc.VirtualMachineAdapter.Scripts.Add(x.DeepCopy()));
                        }
                    }

                }

                // vl added code
                if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Essential))
                {
                    patchNpc.Configuration.Flags = patchNpc.Configuration.Flags.SetFlag(NpcConfiguration.Flag.Essential, true);
                    change = true;
                }

                if (npc.AIData.Aggression != patchNpc.AIData.Aggression)
                {
                    patchNpc.AIData.Aggression = npc.AIData.Aggression;
                    change = true;
                }

                if (npc.CombatStyle.FormKey != patchNpc.CombatStyle.FormKey && npc.CombatStyle.FormKey != null)
                {
                    patchNpc.CombatStyle.FormKey = npc.CombatStyle.FormKey;
                    change = true;
                }

                if (npc.PlayerSkills != null)
                {
                    patchNpc.PlayerSkills = npc.PlayerSkills.DeepCopy();
                    change = true;
                }

                if (npc.Items != null)
                {
                    if (patchNpc.Items == null)
                    {
                        patchNpc.Items = (ExtendedList<ContainerEntry>?)npc.Items;
                    }
                    else
                    {
                        foreach (var item in npc.Items)
                        {
                            if (patchNpc.Items != null)

                            {
                                if (!patchNpc.Items.Select(x => x.Item.Item.FormKey).Contains(item.Item.Item.FormKey))
                                {
                                    patchNpc.Items.Add(item.DeepCopy());
                                }
                            }
                        }
                    }
                }

                if (_settings.Value.IgnoreIdenticalToLastOverride && !change)
                {
                    state.PatchMod.Npcs.Remove(npc);
                }


                b++;
                processed++;
            }
            if (state.PatchMod.ModKey.Name == AioPatchName)
            {
                state.PatchMod.ModHeader.Flags = state.PatchMod.ModHeader.Flags | SkyrimModHeader.HeaderFlag.LightMaster;
            }
        }
    }
}
