using System;
using System.Collections.Generic;
using System.Text;

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

/*
 * Must be unique per each script project.
 * Prevents collisions of multiple `class Program` declarations.
 * Will be used to detect the ingame script region, whose name is the same.
 */
namespace DashboardReplication {

/*
 * Do not change this declaration because this is the game requirement.
 */
public sealed class Program : MyGridProgram {

    /*
     * Must be same as the namespace. Will be used for automatic script export.
     * The code inside this region is the ingame script.
     */
    #region DashboardReplication
    bool enableDebug = true;
    private void Debug(string s, params Object[] args) {
        if (!enableDebug) {
            return;
        }
        Echo(String.Format("DEBUG: " + s, args));
    }

    private void Error(string s, params Object[] args) {
        Echo(String.Format("ERROR: " + s, args));
    }
    private void Warning(string s, params Object[] args) {
        Echo(String.Format("WARNING: " + s, args));
    }

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

    private sealed class TextSurfaceReference {
        public string providerName { get; }
        public int index { get; }
        public int hashCache;

        public TextSurfaceReference(
            string providerName,
            int index 
        ) {
            this.providerName = providerName;
            this.index = index;
        }
    }

    private Dictionary<string, TextSurfaceReference> transmittingDashboards = new Dictionary<string, TextSurfaceReference>() {
    };
    private Dictionary<string, List<TextSurfaceReference>> receivingDashboards = new Dictionary<string, List<TextSurfaceReference>>() {
    };


    StringBuilder textBuffer = new StringBuilder();
    private IMyBroadcastListener[] broadcastListeners;
    private Config config;

    private IMyTextSurface GetSurface(TextSurfaceReference textSurfaceReference) {
            string textSurfaceProviderName = textSurfaceReference.providerName;
            int surfaceIndex = textSurfaceReference.index;

            IMyTerminalBlock block = GridTerminalSystem.GetBlockWithName(textSurfaceProviderName);
            if (block == null) {
                Warning("Block {0} not found.");
                return null;
            }
            if (!(block is IMyTextSurfaceProvider)) {
                Warning("Block {0} is not a TextSurfaceProvider.");
                return null;
            }
            IMyTextSurfaceProvider surfaceProvider = block as IMyTextSurfaceProvider;
            if (surfaceIndex < surfaceProvider.SurfaceCount && surfaceIndex >= 0) {
                return surfaceProvider.GetSurface(surfaceIndex);
            } else {
                Warning("SurfaceProvider {0} does not have TextSurface with an indexc of {1}.",
                    textSurfaceProviderName,
                    surfaceIndex
                );
                return null;
            }
    }

    /*
     * The constructor, called only once every session and always before any 
     * other method is called. Use it to initialize your script. 
     *    
     * The constructor is optional and can be removed if not needed.
     *
     * It's recommended to set RuntimeInfo.UpdateFrequency here, which will 
     * allow your script to run itself without a timer block.
     */
    public Program() {
        config = new Config(Self().CustomData);

        foreach(Config section in config.sections) {
            Debug("Section {0}", section.sectionName);
            if (section.sectionName == "transmitter") {
                Debug("Creating transmitter");
                var block = section.Get("block", "");
                if (block == "") {
                    Warning("Missing block configuration in section.");
                    continue;
                }
                int index = int.Parse(section.Get("surface", "0"));
                TextSurfaceReference textSurfaceReference = new TextSurfaceReference(block, index);
                IMyTextSurface textSurface = GetSurface(textSurfaceReference);
                if (textSurface == null) {
                    continue;
                }
                transmittingDashboards.Add(
                   section.Get("tag", block),
                   textSurfaceReference 
                );
            } else if (section.sectionName == "receiver") {
                Debug("Creating receiver");
                var block = section.Get("block", "");
                if (block == "") {
                    Warning("ERROR: Missing block configuration in section.");
                    continue;
                }
                int index = int.Parse(section.Get("surface", "0"));
                var tag = section.Get("tag", block);
                
                TextSurfaceReference textSurfaceReference = new TextSurfaceReference(block, index);
                IMyTextSurface textSurface = GetSurface(textSurfaceReference);
                if (textSurface == null) {
                    continue;
                }


                if (!receivingDashboards.ContainsKey(tag)) {
                    receivingDashboards.Add(tag, new List<TextSurfaceReference>());
                }
                
                Debug("Creating new Surface Ref: {0}", textSurface);
                receivingDashboards[tag].Add(textSurfaceReference);
                Debug("Added config entry for a {0}, with the tag {1} on block {2}#{3}", section.sectionName, tag, block, index);
            } 
        }

        // Run script once every 100 ticks
        Runtime.UpdateFrequency = UpdateFrequency.Update100;

        broadcastListeners = new IMyBroadcastListener[receivingDashboards.Count];
        var i = 0;
        foreach(var kv in receivingDashboards) {
            broadcastListeners[i++] = IGC.RegisterBroadcastListener(kv.Key);
        }
    }

    /*
     * Called when the program needs to save its state. Use this method to save
     * your state to the Storage field or some other means. 
     * 
     * This method is optional and can be removed if not needed.
     */
    public void Save() {}

    /*
     * The main entry point of the script, invoked every time one of the 
     * programmable block's Run actions are invoked, or the script updates 
     * itself. The updateSource argument describes where the update came from.
     * 
     * The method itself is required, but the arguments above can be removed 
     * if not needed.
     */
    public void Main(string argument, UpdateType updateSource) {
        foreach(var entry in transmittingDashboards) {
            TextSurfaceReference surfaceReference = entry.Value;
            string tag = entry.Key;
            IMyTextSurface surface = GetSurface(surfaceReference);

            Debug("Transmitting Surface: {0} {1}", surfaceReference, surface);

            if (surface == null) {
                continue;
            }

            surface.ReadText(textBuffer);
            string text = textBuffer.ToString();

            // Reduce the amout of message posting by only
            // posting a message if the HashCode has changed.
            // Note that this is only a 32 bit hash and could
            // potentially suffer from collisions quite frequently,
            // leading to updates not being posted. For now, this
            // is acceptable to me.
            int hash = text.GetHashCode();
            if (surfaceReference.hashCache == hash) {
                continue;
            }

            IGC.SendBroadcastMessage(tag, text);
            surfaceReference.hashCache = hash;
        }

        foreach(var listener in broadcastListeners) {
            while (listener.HasPendingMessage) {
                var myMessage = listener.AcceptMessage();
                if (!receivingDashboards.ContainsKey(myMessage.Tag)) {
                    continue;
                }

                var surfaceReferences =
                    receivingDashboards[myMessage.Tag];

                foreach(var surfaceReference in surfaceReferences) {
                    IMyTextSurface surface = GetSurface(surfaceReference);
                    Debug("Receiving Surface: Ref: {0} Surf: {1}", surfaceReference, surface);

                    if (surface == null) {
                        continue;
                    }

                    if (!(myMessage.Data is string)) {
                        continue;
                    }

                    surface.WriteText(
                        myMessage.Data.ToString(),
                        false
                    );
                }
            }
        }
    }

    #endregion // DashboardReplication
}}