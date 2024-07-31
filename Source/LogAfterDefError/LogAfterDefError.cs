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

		internal static void Clear() {
			Patches.Clear();
			Operations.Clear();
			CurrentAsset.Dispose();
			PatchTracer.Dispose();
		}
	}

	public class LogAfterDefErrorMod : Mod {

		internal static bool hasExpection = false;

		public LogAfterDefErrorMod(ModContentPack content) : base(content) {
			GetSettings<LogAfterDefErrorModSettings>();
			RuntimeHelpers.RunClassConstructor(typeof(HarmonyPatches).TypeHandle);
			LongEventHandler.ExecuteWhenFinished(() => {
				Utility.Clear();
				HarmonyPatches.RemovePatches();
				HarmonyPatches.PostPatch();
			});
		}

		public override void DoSettingsWindowContents(Rect inRect) {
			var listing = new Listing_Standard();
			listing.Begin(inRect);
			var enabled = LogAfterDefErrorModSettings.DefTraceEnabled;
			listing.CheckboxLabeled("LogAfterDefError.Settings.DefTraceEnabled".Translate(), ref enabled);
			LogAfterDefErrorModSettings.DefTraceEnabled = enabled;
			if(LogAfterDefErrorModSettings.DefTraceEnabled) {
				enabled = LogAfterDefErrorModSettings.PatchTraceEnabled;
				listing.CheckboxLabeled("LogAfterDefError.Settings.PatchTraceEnabled".Translate(), ref enabled);
				LogAfterDefErrorModSettings.PatchTraceEnabled = enabled;
			}
			listing.End();
		}

		public override string SettingsCategory() {
			return "LogAfterDefError.Settings".Translate();
		}
	}

	public class LogAfterDefErrorModSettings : ModSettings {

		private static bool defTraceEnabled = true;
		private static bool patchTraceEnabled = true;

		public static bool DefTraceEnabled { get { return defTraceEnabled; }  set { defTraceEnabled = value; } }
		public static bool PatchTraceEnabled { get { return patchTraceEnabled && defTraceEnabled; } set { patchTraceEnabled = value; } }

		internal const int version = 0;

		public override void ExposeData() {
			Scribe_Values.Look(ref defTraceEnabled, "defTraceEnabled", true);
			Scribe_Values.Look(ref patchTraceEnabled, "patchTraceEnabled", true);
		}

	}

}
