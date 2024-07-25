using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml;
using UnityEngine;
using Verse;

namespace LogAfterDefError {

	internal static class Utility {
		internal static readonly Dictionary<PatchOperation, LoadableXmlAsset> Patches = [];
		internal static readonly Dictionary<XmlNode, List<PatchOperation>> Operations = [];
		internal static readonly ThreadLocal<LoadableXmlAsset> CurrentAsset = new();
		internal static readonly ThreadLocal<Stack<PatchOperation>> PatchTracer = new(() => new Stack<PatchOperation>());
		internal static string ToStringFull(this object obj) {
			return obj.GetType().FullName + ":" + obj.GetType().GetRuntimeFields().Join(x => x.Name + "=" + x.GetValue(obj));
		}

		internal static string FormatAsset(LoadableXmlAsset asset) {
			var mod = asset?.mod ?? null;
			return $"{mod?.Name}{asset?.FullFilePath?.Replace(mod?.RootDir, "")}";
		}

		internal static string FormatAsset(string name, LoadableXmlAsset asset) {
			return (name.NullOrEmpty() ? "" : name + ":") + FormatAsset(asset);
		}
	}

	public class LogAfterDefErrorMod : Mod {

		public LogAfterDefErrorMod(ModContentPack content) : base(content) {
			GetSettings<LogAfterDefErrorModSettings>();
			RuntimeHelpers.RunClassConstructor(typeof(HarmonyPatches).TypeHandle);
			LongEventHandler.ExecuteWhenFinished(() => {
				HarmonyPatches.RemovePatches();
				Utility.Patches.Clear();
				Utility.Operations.Clear();
				Utility.CurrentAsset.Dispose();
				Utility.PatchTracer.Dispose();
			});
		}

		public override void DoSettingsWindowContents(Rect inRect) {
			var listing = new Listing_Standard();
			listing.Begin(inRect);
			listing.CheckboxLabeled("LogAfterDefError.Settings.DefTraceEnabled".Translate(), ref LogAfterDefErrorModSettings.defTraceEnabled);
			if(LogAfterDefErrorModSettings.defTraceEnabled) {
				listing.CheckboxLabeled("LogAfterDefError.Settings.PatchTraceEnabled".Translate(), ref LogAfterDefErrorModSettings.patchTraceEnabled);
			}
			listing.End();
		}

		public override string SettingsCategory() {
			return "LogAfterDefError.Settings".Translate();
		}
	}

	public class LogAfterDefErrorModSettings : ModSettings {

		public static bool defTraceEnabled = true;
		public static bool patchTraceEnabled = false;

		public override void ExposeData() {
			Scribe_Values.Look(ref defTraceEnabled, "defTraceEnabled", true);
			Scribe_Values.Look(ref patchTraceEnabled, "patchTraceEnabled", false);
		}

	}

}
