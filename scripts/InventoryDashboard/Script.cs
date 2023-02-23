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

    private List<KeyValuePair<string, int>> oreDashboardSurfaceIdentifiers =
     new List<KeyValuePair<string, int>>() { };
    private List<KeyValuePair<string, int>> componentDashboardSurfaceIdentifiers =
     new List<KeyValuePair<string, int>>() { };
    // List of subtypes to filter the display for
    private List<string> oreSubtypeFilter = new List<string>{ 
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
    private List<string> componentSubtypeFilter = new List<string>{ 
        "SteelPlate",
        "InteriorPlate",
        "Construction",
        "Motor",
        "LargeTube",
        "SmallTube",
        "Computer",
        "Girder",
        "MetalGrid",
        "Display",
        "BulletproofGlass",
        "RadioCommunication",
        "Detector",
        "Medical",
        "GravityGenerator",
    };

    private Dictionary<string, string> componentTranslation = new Dictionary<string, string> {
        { "SteelPlate", "Steel"},
        { "InteriorPlate", "Interior"},
        { "Construction", "Constr." },
        { "BulletproofGlass", "Glass"},
        { "RadioCommunication", "Radio Com."},
        { "GravityGenerator", "Gravity"},
        { "LargeTube", "Large Tube"},
        { "SmallTube", "Small Tube"},
        { "MetalGrid", "Metal Grid"},
    };

    private Dictionary<string, int> componentTargets = new Dictionary<string, int>();

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

    private List<IMyProductionBlock> assemblers = new List<IMyProductionBlock>();

    private IMyTextSurface[] oreDashboardSurfaces;
    private IMyTextSurface[] componentDashboardSurfaces;

    private string typePrefix = "MyObjectBuilder_";
    private string GetOreKey(string material) {
        return typePrefix + "Ore:" + material;
    }

    private string GetIngotKey(string material) {
        return typePrefix + "Ingot:" + material;
    }

    private string GetComponentKey(string material) {
        return typePrefix + "Component:" + material;
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
    private MyFixedPoint GetComponentAmount(string material) {
        string key = GetComponentKey(material);
        if (inventoryDict.ContainsKey(key)) {
            return inventoryDict[key];
        }
        return 0;
    }

    private IMyTextSurface getSurfaceFromIdentifier(KeyValuePair<string, int> identifier) {
            var consoleName = identifier.Key;
            var displayIndex = identifier.Value;

            var console = GridTerminalSystem.GetBlockWithName(consoleName);
            if (console == null) {
                Echo(String.Format("ERROR: Failed to find block {0}", consoleName));
                return null;
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
                    return null;
                }
                textSurface = textSurfaceProvider.GetSurface(displayIndex);
            }

            if (textSurface == null) {
                Echo(String.Format("ERROR: Block {0} is not a text surface provider.", consoleName));
                return null;
            }

            // Set textSurface to correct ContentType and apply Monospace font
            textSurface.ContentType = ContentType.TEXT_AND_IMAGE;
            textSurface.Font = "Monospace";

            return textSurface;
    }

    public void Main(string argument, UpdateType updateSource)
    {
        oreDashboardSurfaces = new IMyTextSurface[oreDashboardSurfaceIdentifiers.Count];
        componentDashboardSurfaces = new IMyTextSurface[componentDashboardSurfaceIdentifiers.Count];

        MyFixedPoint totalVolume = 0;
        MyFixedPoint usedVolume = 0;
        int i = 0;
        foreach(var kv in oreDashboardSurfaceIdentifiers) {
            var surface = this.getSurfaceFromIdentifier(kv);

            if (surface == null) {
                return;
            }

            oreDashboardSurfaces[i++] = surface;
        }
        
        i = 0;
        foreach(var kv in componentDashboardSurfaceIdentifiers) {
            var surface = this.getSurfaceFromIdentifier(kv);

            if (surface == null) {
                return;
            }

            componentDashboardSurfaces[i++] = surface;
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
                if (
                    !oreSubtypeFilter.Exists((filteredSubtype) => itemIdentifier.Contains(filteredSubtype)) &&
                    !componentSubtypeFilter.Exists((filteredSubtype) => itemIdentifier.Contains(filteredSubtype))
                ) {
                    continue;
                }

                // initialize dict slot if we haven't seen this identifier yet
                if (!inventoryDict.ContainsKey(itemIdentifier)) {
                    inventoryDict[itemIdentifier] = 0;
                }

                // add amount of this identifier in this inventory to amount we've already seen
                inventoryDict[itemIdentifier] = inventoryDict[itemIdentifier] + item.Amount;
            }
            
        }

        string priorityComponent = "";
        assemblers.Clear();
        GridTerminalSystem.GetBlocksOfType<IMyProductionBlock>(assemblers);
        bool assemblerBusy = assemblers.Exists((IMyProductionBlock block) => !block.IsQueueEmpty);
        bool assemblerError = false;

        if (assembler != null) {
            foreach(var component in componentSubtypeFilter) {
                MyFixedPoint componentCount = inventoryDict.GetValueOrDefault(GetComponentKey(component), 0);
                int componentTarget = componentTargets.GetValueOrDefault(component, 0);
                var diff = componentTarget - componentCount;
                if (diff > 0) {
                    priorityComponent = component;

                    if (assemblerBusy) {
                        break;
                    }

                    string blueprintName = getComponentBlueprintName(component);
                    MyDefinitionId blueprint = MyDefinitionId.Parse(blueprintName);
                    if (assembler.CanUseBlueprint(blueprint)) {
                        assembler.AddQueueItem(blueprint, (MyFixedPoint) Math.Min(diff.ToIntSafe(), 100));
                        break;
                    } else {
                        assemblerError = true;
                        Echo(String.Format("ERROR: Assembler {0} can't use Blueprint {1}", assembler.CustomName, blueprint));
                    }
                }
            }
        }


        foreach (IMyTextSurface textSurface in oreDashboardSurfaces) {
            writeOreDashboard(textSurface, totalVolume, usedVolume);
        }
        
        foreach (IMyTextSurface textSurface in componentDashboardSurfaces) {
            writeComponentDashboard(textSurface, priorityComponent, assemblerError);
        }
    }

    private string getComponentBlueprintName(string component) {
        switch (component) {
            case "Girder":
            case "Construction":
            case "Computer":
            case "Motor":
            case "Detector":
            case "Medical":
            case "RadioCommunication":
                component = component + "Component";
                break;
        }
        return "MyObjectBuilder_BlueprintDefinition/" + component;
    }

    private string getComponentDisplayName(string component) {
        return componentTranslation.GetValueOrDefault(component, component);
    }

    private void writeComponentDashboard(IMyTextSurface textSurface, string priorityComponent, bool assemblerError) {
        string formatString = "{0,-10}{1,7}{3,1}{2,7}\n";

        textSurface.WriteText(String.Format(formatString + "\n", "Component", "Target", "Count",""), false);

        componentSubtypeFilter.Sort((l, r) => {
            MyFixedPoint lAmount = GetComponentAmount(l);
            MyFixedPoint rAmount = GetComponentAmount(r);

            return (rAmount - lAmount).ToIntSafe();
        });

        // for each material in the subtype list
        foreach(string material in componentSubtypeFilter) {
            MyFixedPoint components = GetComponentAmount(material);
            
            int target = componentTargets.GetValueOrDefault(material, 0);
            
            string s = String.Format(
                formatString, 
                getComponentDisplayName(material),
                target > 0 ? formatAmount(target) : "",
                formatAmount(components),
                priorityComponent == material ? assemblerError ? "!" : "*" : ""
            );
            
            textSurface.WriteText(s, true);
        }
    }

    private void writeOreDashboard(IMyTextSurface textSurface, MyFixedPoint totalVolume, MyFixedPoint usedVolume) {
        string formatString = "{0,-10}{1,7} {2,7}\n";
        string lastRowFormatString = "\n{0,20:0%} {1}\n";

        // Write heading row
        textSurface.WriteText(String.Format(formatString + "\n", "Material", "Ingots", "Ore"), false);


        if (enableAutoSort) {
            oreSubtypeFilter.Sort((l, r) => {
                MyFixedPoint lAmount = GetOreAmount(l) + GetIngotAmount(l);
                MyFixedPoint rAmount = GetOreAmount(r) + GetIngotAmount(r);

                return (rAmount - lAmount).ToIntSafe();
            });
        }

        // for each material in the subtype list
        foreach(string material in oreSubtypeFilter) {
            // Initialize ore and ingot amount
            MyFixedPoint ore = 0;
            MyFixedPoint ingots = 0;

            ore += GetOreAmount(material);
            ingots += GetIngotAmount(material);
            
            // Write a line for the material including the material name and amounts of ingots and ore
            string s = String.Format(formatString, material, formatAmount(ingots), formatAmount(ore));
            if (material == "Stone" || material == "Ice") {
                s = String.Format(formatString, material, "", formatAmount(ore));
            }
            textSurface.WriteText(s, true);
        }
        
        textSurface.WriteText(String.Format(lastRowFormatString, (float)usedVolume/(float)totalVolume,  "Used"), true);
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

    private IMyProductionBlock assembler = null;
    public Program()
    {
        // Run script once every 100 ticks
        Runtime.UpdateFrequency = UpdateFrequency.Update100;

        Config config = new Config(Self().CustomData);

        enableLocalGridOnly = config.Get("localGridOnly", "false") == "true";
        enableAutoSort = config.Get("autoSort", "false") == "true";

        string assemblerName = config.Get("assembler", "");

        if (assemblerName != "") {
            assembler = GridTerminalSystem.GetBlockWithName(assemblerName) as IMyProductionBlock;
        }

        foreach(Config section in config.sections) {
            switch(section.sectionName) {
                case "dashboard":
                    string blockName = section.Get("block", "");
                    if (blockName == "") {
                        continue;
                    }

                    string dashboardType = section.Get("type", "ore");

                    int index = int.Parse(section.Get("surface", "0"));

                    KeyValuePair<string, int> kv = new KeyValuePair<string, int>(blockName, index);
                    switch (dashboardType) {
                        case "ore":
                            Echo(String.Format("Ore Dashboard {0}", kv));
                            oreDashboardSurfaceIdentifiers.Add(kv);
                            break;
                        case "components":
                            Echo(String.Format("Component Dashboard {0}", kv));
                            componentDashboardSurfaceIdentifiers.Add(kv);
                            break;
                        default:
                            Echo(String.Format("No Type Dashboard {0}", kv));
                            break;
                    }
                    break;
                
                case "targets":
                    foreach(var component in componentSubtypeFilter) {
                        int target = int.Parse(section.Get(component, "0"));
                        if (target > 0) {
                            componentTargets.Add(component, target);
                        }
                    }
                    break;
            }
        }

        if (oreDashboardSurfaceIdentifiers.Count == 0) {
            // Keep the default value for backwards compatibility
            oreDashboardSurfaceIdentifiers.Add(
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