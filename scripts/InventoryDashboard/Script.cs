using System;

// Space Engineers DLLs
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using VRageMath;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ObjectBuilders.Definitions;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;

/*
 * Must be unique per each script project.
 * Prevents collisions of multiple `class Program` declarations.
 * Will be used to detect the ingame script region, whose name is the same.
 */
namespace InventoryDashboard {

/*
 * Do not change this declaration because this is the game requirement.
 */
public sealed class Program : MyGridProgram {

    /*
     * Must be same as the namespace. Will be used for automatic script export.
     * The code inside this region is the ingame script.
     */
    #region InventoryDashboard

    private List<KeyValuePair<string, int>> textSurfaceIdentifiers =
     new List<KeyValuePair<string, int>>() { };
    // List of subtypes to filter the display for
    // Only Ore and Ingot types supported
    private List<string> subtypeFilter = new List<string>{ 
            "Ice",
            "Stone",
            "Iron",
            "Silicon",
            "Nickel",
            "Cobalt",
            "Magnesium",
            "Silver",
            "Gold",
            "Uranium",
            "Platinum",
    };
    IMyProgrammableBlock self = null;
    public IMyProgrammableBlock Self() {
        if (self != null) {
            return self;
        }
        List<IMyProgrammableBlock> list = new List<IMyProgrammableBlock>();
        GridTerminalSystem.GetBlocksOfType<IMyProgrammableBlock>(list, block => block.IsRunning);

        if (list.Count != 1) {
            throw new Exception("PANIC: Failed to retrieve Programmable Block.");
        }

        self = list[0];

        return self;
    }

    public class Config {
        public string sectionName { get; }
        private Dictionary<string, string> config;
        public List<Config> sections { get; } = new List<Config>();

        private System.Text.RegularExpressions.Regex sectionRe = new System.Text.RegularExpressions.Regex(@"^\[(.+)\]$");

        private Config(IEnumerator<string> lines, string sectionName) {
            this.sectionName = sectionName;
            config = new Dictionary<string, string>();
            while(lines.MoveNext()) {
                string line = lines.Current.Trim();

                if (sectionName != "" && line == "") {
                    // if we encounter an empty line in a section
                    // close the section.

                    break;
                }

                var match = sectionRe.Match(line);
                if (match.Success) {
                    // If we encounter a section within a section, then
                    // throw an exception
                    if (sectionName != "") {
                        throw new Exception("PANIC: Found a section within a section. Did you forget to close a section with an empty line?");
                    }

                    sections.Add(new Config(lines, match.Groups[1].Value));

                    continue;
                }

                string[] kv = line.Split('=');
                if (kv.Length > 1) {
                    config.Add(kv[0].Trim(), kv[1].Trim());
                } else {
                    config.Add(kv[0].Trim(), "");
                }
            };
        }
        public Config(string config) : this((System.Text.RegularExpressions.Regex.Split(config, @"\r?\n|\r") as IEnumerable<string>).GetEnumerator(), "") {
        }

        public string Get(string key, string defaultValue) {
            return config.GetValueOrDefault(key, defaultValue);
        }
    }


    private List<IMyTerminalBlock> entityList = new List<IMyTerminalBlock>();
    private Dictionary<string, MyFixedPoint> inventoryDict = new Dictionary<string, MyFixedPoint>();
    private List<MyInventoryItem> itemList = new List<MyInventoryItem>();

    private IMyTextSurface[] textSurfaces;

    private void WriteText(string value, bool append = false)
    {
        foreach (IMyTextSurface textSurface in textSurfaces) {
            textSurface.WriteText(value, append);
        }
    }

    private string typePrefix = "MyObjectBuilder_";
    private string GetOreKey(string material) {
        return typePrefix + "Ore:" + material;
    }

    private string GetIngotKey(string material) {
        return typePrefix + "Ingot:" + material;
    }

    private MyFixedPoint GetOreAmount(string material) {
        string key = GetOreKey(material);
        if (inventoryDict.ContainsKey(key)) {
            return inventoryDict[key];
        }
        return 0;
    }
    private MyFixedPoint GetIngotAmount(string material) {
        string key = GetIngotKey(material);
        if (inventoryDict.ContainsKey(key)) {
            return inventoryDict[key];
        }
        return 0;
    }

    public void Main(string argument, UpdateType updateSource)
    {
        textSurfaces = new IMyTextSurface[textSurfaceIdentifiers.Count];

        MyFixedPoint totalVolume = 0;
        MyFixedPoint usedVolume = 0;
        int i = 0;
        foreach(var kv in textSurfaceIdentifiers) {
            var consoleName = kv.Key;
            var displayIndex = kv.Value;

            var console = GridTerminalSystem.GetBlockWithName(consoleName);
            if (console == null) {
                Echo(String.Format("ERROR: Failed to find block {0}", consoleName));
                return;
            }

            IMyTextSurface textSurface = null;

            if (console is IMyTextSurfaceProvider) {
                var textSurfaceProvider = console as IMyTextSurfaceProvider;
                if (displayIndex < 0 || displayIndex >= textSurfaceProvider.SurfaceCount) {
                    Echo(String.Format(
                        "ERROR: Failed to find display {0} on text surface provider {1}",
                        displayIndex,
                        consoleName
                    ));
                    return;
                }
                textSurface = textSurfaceProvider.GetSurface(displayIndex);
            }

            if (textSurface == null) {
                Echo(String.Format("ERROR: Block {0} is not a text surface provider.", consoleName));
                return;
            }

            // Set textSurface to correct ContentType and apply Monospace font
            textSurface.ContentType = ContentType.TEXT_AND_IMAGE;
            textSurface.Font = "Monospace";

            textSurfaces[i++] = textSurface;
        }

        // Get list of containers
        entityList.Clear();
        GridTerminalSystem.GetBlocksOfType<IMyEntity>(entityList);

        // Clear existing inventory dictionary.
        inventoryDict.Clear();

        // Loop over all entities
        foreach(var entity in entityList) {
            // Skip block if it does not have an inventory
            if (!entity.HasInventory) {
                continue;
            }

            if (enableLocalGridOnly && (
                    !(entity is IMyCubeBlock) ||
                    (entity as IMyCubeBlock).CubeGrid != Self().CubeGrid
            )) {
                continue;
            }

            itemList.Clear();

            // Look at all inventories in each entity
            for (var j = 0; j < entity.InventoryCount; j++) {
                var inventory = entity.GetInventory(j);

                totalVolume += inventory.MaxVolume;
                usedVolume += inventory.CurrentVolume;

                // Pull all items in this inventory into the itemList 
                inventory.GetItems(itemList);
            }

            // Loop over each item in the inventory
            foreach (MyInventoryItem item in itemList) {
                // Build an item type identifier to store in a dict
                var itemIdentifier = item.Type.TypeId + ":" + item.Type.SubtypeId;

                // If the subtype isn't one of the ones we are looking for, skip
                if (!subtypeFilter.Exists((filteredSubtype) => itemIdentifier.Contains(filteredSubtype))) {
                    continue;
                }

                // initialize dict slot if we haven't seen this identifier yet
                if (!inventoryDict.ContainsKey(itemIdentifier)) {
                    inventoryDict[itemIdentifier] = 0;
                }

                // add amount of this identifier in this invenetory to amount we've already seen
                inventoryDict[itemIdentifier] = inventoryDict[itemIdentifier] + item.Amount;
            }
            
        }

        string formatString = "{0,-10}{1,7} {2,7}\n";
        string lastRowFormatString = "\n{0,20:0%} {1}\n";


        // Write heading row
        WriteText(String.Format(formatString + "\n", "Material", "Ingots", "Ore"), false);


        if (enableAutoSort) {
            subtypeFilter.Sort((l, r) => {
                MyFixedPoint lAmount = GetOreAmount(l) + GetIngotAmount(l);
                MyFixedPoint rAmount = GetOreAmount(r) + GetIngotAmount(r);

                return (rAmount - lAmount).ToIntSafe();
            });
        }

        // for each material in the subtype list
        foreach(string material in subtypeFilter) {
            // Initialize ore and ingot amount
            MyFixedPoint ore = 0;
            MyFixedPoint ingots = 0;

            ore += GetOreAmount(material);
            ingots += GetIngotAmount(material);
            
            // Write a line for the material inlcuding the material name and amounts of ingots and ore
            string s = String.Format(formatString, material, formatAmount(ingots), formatAmount(ore));
            if (material == "Stone" || material == "Ice") {
                s = String.Format(formatString, material, "", formatAmount(ore));
            }
            WriteText(s, true);
        }
        
        WriteText(String.Format(lastRowFormatString, (float)usedVolume/(float)totalVolume,  "Used"), true);
    }


    private string formatAmount(MyFixedPoint amount) {
        if (amount >= 1000000000) {
            return String.Format("{0:F1}g", amount.ToIntSafe() / 1000000000f);
        }
        if (amount >= 1000000) {
            return String.Format("{0:F1}m", amount.ToIntSafe() / 1000000f);
        }
        if (amount >= 1000) {
            return String.Format("{0:F1}k", amount.ToIntSafe() / 1000f);
        }
        return String.Format("{0:0.#}", (float) amount);
    }

    private bool enableLocalGridOnly = false;
    private bool enableAutoSort = false;
    public Program()
    {
        // Run script once every 100 ticks
        Runtime.UpdateFrequency = UpdateFrequency.Update100;

        Config config = new Config(Self().CustomData);

        enableLocalGridOnly = config.Get("localGridOnly", "false") == "true";
        enableAutoSort = config.Get("autoSort", "false") == "true";

        foreach(Config section in config.sections) {
            if (section.sectionName != "dashboard") {
                continue;
            }

            string blockName = section.Get("block", "");
            if (blockName == "") {
                continue;
            }

            int index = int.Parse(section.Get("surface", "0"));

            textSurfaceIdentifiers.Add(new KeyValuePair<string, int>(blockName, index));
        }

        if (textSurfaceIdentifiers.Count == 0) {
            // Keep the default value for backwards compatibility
            textSurfaceIdentifiers.Add(
                new KeyValuePair<string, int>(
                    "Inventory Dashboard",
                     0
                )
            );
        }
    }

    public void Save()
    {
        // Called when the program needs to save its state. Use
        // this method to save your state to the Storage field
        // or some other means. 
        // 
        // This method is optional and can be removed if not
        // needed.
    }


    #endregion // InventoryDashboard
}}