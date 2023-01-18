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

    // LCD Panel to be used to display inventory
    // Set panel to Mono font for best result
    private string consoleName =  "Inventory Dashboard";

    // List of subtypes to filter the display for
    // Only Ore and Ingot types supported
    private List<string> subtypeFilter = new List<string>{ 
            "Ice",
            "Stone",
            "Iron",
            "Silicon",
            "Nickel",
            "Cobalt",
            "Magensium",
            "Silver",
            "Gold",
            "Uranium",
            "Platinum",
    };

    private List<IMyTerminalBlock> entityList = new List<IMyTerminalBlock>();
    private Dictionary<string, MyFixedPoint> inventoryDict = new Dictionary<string, MyFixedPoint>();
    private List<MyInventoryItem> itemList = new List<MyInventoryItem>();

    public void Main(string argument, UpdateType updateSource)
    {
        MyFixedPoint totalVolume = 0;
        MyFixedPoint usedVolume = 0;

        var console = GridTerminalSystem.GetBlockWithName(consoleName) as IMyTextPanel;
        if (console == null) {
            Echo(String.Format("ERROR: Failed to find text panel {0}", consoleName));
            return;
        }

        // Set console to correct ContentType and apply Monospace font
        console.ContentType = ContentType.TEXT_AND_IMAGE;
        console.Font = "Monospace";

        // Get list of containers
        entityList.Clear();
        GridTerminalSystem.GetBlocksOfType<IMyEntity>(entityList);

        // Clear existing inventory dictionary.
        inventoryDict.Clear();

        // Loop over all entities
        for (var i = 0; i < entityList.Count; i++) {
            var entity = entityList[i] as IMyEntity;

            // Skip block if it does not have an inventory
            if (!entity.HasInventory) {
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

        string typePrefix = "MyObjectBuilder_";
        string formatString = "{0,-10}{1,7} {2,7}\n";
        string lastRowFormatString = "\n{0,20:0%} {1}\n";


        // Write heading row
        console.WriteText(String.Format(formatString + "\n", "Material", "Ingots", "Ore"), false);

        // for each material in the subtype list
        foreach(string material in subtypeFilter) {
            // Initialize ore and ingot amount
            MyFixedPoint ore = 0;
            MyFixedPoint ingots = 0;

            // Build identifier strings for material Ore and Ingots
            string oreKey = typePrefix + "Ore:" + material;
            string ingotsKey = typePrefix + "Ingot:" + material;

            // If we have any, grab the amount of Ore we have
            if (inventoryDict.ContainsKey(oreKey)) {
                ore = inventoryDict[oreKey];
            }
            // If we have any grab the amount of ingots we have
            if (inventoryDict.ContainsKey(ingotsKey)) {
                ingots = inventoryDict[ingotsKey];
            }
            
            // Write a line for the material inlcuding the material name and amounts of ingots and ore
            string s = String.Format(formatString, material, formatAmount(ingots), formatAmount(ore));
            if (material == "Stone" || material == "Ice") {
                s = String.Format(formatString, material, "", formatAmount(ore));
            }
            console.WriteText(s, true);
        }
        
        console.WriteText(String.Format(lastRowFormatString, (float)usedVolume/(float)totalVolume,  "Used"), true);
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

    public Program()
    {
        // Run script once every 100 ticks
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
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