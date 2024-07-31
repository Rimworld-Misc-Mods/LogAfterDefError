using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;
using System.Reflection.Emit;
using System.Xml;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using UnityEngine.Assertions.Must;
using System;
using System.Runtime.CompilerServices;

namespace LogAfterDefError {

	internal static class HarmonyPatches {
		internal static Harmony Instance { get; private set; }

		static HarmonyPatches() {
			Instance = new Harmony("ordpus.logafterdeferror");
			PatchExp();
			Instance.PatchAll(Assembly.GetExecutingAssembly());
		}

		internal static void RemovePatches() {
			Instance.UnpatchAll(Instance.Id);
		}

		internal static void PostPatch() {
			RimWorld__MainMenuDrawer__MainMenuOnGUI.Patch(Instance);
		}

		private static void PatchExp() {
			Assembly.GetExecutingAssembly()
				.GetTypes()
				.Where(x => x.HasAttribute<HarmonyPatch>())
				.SelectMany(x => x.GetDeclaredMethods())
				.Where(x => x.Name == "Prefix" || x.Name == "Postfix" || x.HasAttribute<HarmonyPrefix>() || x.HasAttribute<HarmonyPostfix>())
				.ToList().ForEach(x => Instance.Patch(x, finalizer: new HarmonyMethod(typeof(HarmonyPatches), nameof(ExceptionWrapper))));
		}

		internal static Exception ExceptionWrapper(Exception __exception) {
			if(__exception != null) {
				LogAfterDefErrorMod.hasExpection = true;
				Log.Error(__exception.StackTrace);
			}
			return null;
		}
	}

	[HarmonyPatch(typeof(LoadedModManager), nameof(LoadedModManager.ParseAndProcessXML))]
	internal static class Verse__LoadedModManager__ParseAndProcessXML {
		internal static bool Prepare() => LogAfterDefErrorModSettings.DefTraceEnabled;
		internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il) {
			var list = instructions.ToList();
			var contentMod = typeof(LoadableXmlAsset).Field(nameof(LoadableXmlAsset.mod));
			var tryRegister = typeof(XmlInheritance).Method(nameof(XmlInheritance.TryRegister));
			var localLog = il.DeclareLocal(typeof(LogMessage));
			for(int i = 0; i < list.Count; ++i) {
				var code = list[i];
				if(code.Calls(tryRegister)) {
					yield return new CodeInstruction(OpCodes.Ldsfld, typeof(Log).Field(nameof(Log.messageQueue))).WithLabels(code.labels);
					yield return new CodeInstruction(OpCodes.Ldfld, typeof(LogMessageQueue).Field(nameof(LogMessageQueue.lastMessage)));
					yield return new CodeInstruction(OpCodes.Stloc_S, localLog);
					code.labels = null;
				}
				yield return code;
				if(code.Calls(tryRegister)) {
					yield return new CodeInstruction(OpCodes.Ldloc_S, 5);
					yield return new CodeInstruction(OpCodes.Ldloc_S, localLog);
					yield return new CodeInstruction(OpCodes.Call, typeof(Verse__LoadedModManager__ParseAndProcessXML).Method(nameof(LoadPostfix)));
				}
			}
		}


		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void LoadPostfix(LoadableXmlAsset xmlAsset, LogMessage __state) {
			if(!LogAfterDefErrorModSettings.DefTraceEnabled || Log.messageQueue.lastMessage == __state) return;
			Log.Message($"[Def Error] {Utility.FormatAsset(xmlAsset)}");
		}
	}

	[HarmonyPatch(typeof(XmlInheritance), nameof(XmlInheritance.TryRegisterAllFrom))]
	internal static class Verse__XmlInheritance__TryRegisterAllFrom {
		internal static bool Prepare() => LogAfterDefErrorModSettings.DefTraceEnabled;
		internal static void Prefix(ref LogMessage __state) {
			__state = Log.messageQueue.lastMessage;
		}

		internal static void Postfix(LoadableXmlAsset xmlAsset, LogMessage __state) {
			if(!LogAfterDefErrorModSettings.DefTraceEnabled || Log.messageQueue.lastMessage == __state) return;
			var defError = $"[Def Error] " + Utility.FormatAsset(xmlAsset);
			Log.Message(defError);
		}
	}


	[HarmonyPatch(typeof(DirectXmlLoader), nameof(DirectXmlLoader.DefFromNode))]
	internal static class Verse__DirectXmlLoader__DefFromNode {
		internal static bool Prepare() => LogAfterDefErrorModSettings.DefTraceEnabled;

		internal static void Prefix(ref LogMessage __state) {
			__state = Log.messageQueue.lastMessage;
		}

		internal static void Postfix(Def __result, XmlNode node, LoadableXmlAsset loadingAsset, LogMessage __state) {
			if(__result == null || !LogAfterDefErrorModSettings.DefTraceEnabled || Log.messageQueue.lastMessage == __state) return;
			if(Log.messageCount > 900) Log.ResetMessageCount();
			var defError = $"[Def Error] " + Utility.FormatAsset(__result.defName, loadingAsset);
			Log.Message(defError + PatchTracking(node));
		}

		internal static string PatchTracking(XmlNode node) {
			if(!LogAfterDefErrorModSettings.PatchTraceEnabled) return "\n" + "LogAfterDefError.PleaseEnablePatchTracking".Translate();
			var res = "";
			var operations = GetParentNodes(node, [node])
				.Where(Utility.Operations.ContainsKey)
				.SelectMany(x => Utility.Operations[x])
				.GroupBy(Utility.Patches.ContainsKey)
				.ToDictionary(k => k.Key, v => v.ToList());
			if(operations.TryGetValue(true, out var list1) && !list1.EnumerableNullOrEmpty()) {
				res += "\nPossible Related Patches ::\n  " + string.Join("\n  ",
					list1.Select(x => Utility.FormatAsset(x.GetType().FullName, Utility.Patches[x])).Distinct());
			}
			if(operations.TryGetValue(false, out var list2) && !list2.EnumerableNullOrEmpty()) {
				res += "\nUnrecognized Patches ::\n  " + string.Join("\n  ", list2.Select(Utility.ToStringFull).Distinct());
			}
			return res;
		}

		internal static List<XmlNode> GetParentNodes(XmlNode node, List<XmlNode> result) {
			if(XmlInheritance.resolvedNodes.TryGetValue(node, out var value) && value.parent != null) {
				result.Add(value.parent.xmlNode);
				GetParentNodes(value.parent.xmlNode, result);
			}
			return result;
		}
	}

	[HarmonyPatch(typeof(Activator), nameof(Activator.CreateInstance), [typeof(Type)])]
	internal static class System__Activator__CreateInstance {
		internal static bool Prepare() => LogAfterDefErrorModSettings.PatchTraceEnabled;
		internal static void Postfix(Type? type, object __result) {
			if(type == null || (__result is not PatchOperation operation)) return;
			Utility.Patches[operation] = Utility.CurrentAsset.Value;
		}
	}

	[HarmonyPatch(typeof(ModContentPack), nameof(ModContentPack.LoadPatches))]
	internal static class Verse__ModContentPack__LoadPatches {
		internal static bool Prepare() => LogAfterDefErrorModSettings.PatchTraceEnabled;
		internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) {
			var getValue = typeof(List<LoadableXmlAsset>).PropertyGetter("Item");
			var setValue = typeof(Verse__ModContentPack__LoadPatches).Method(nameof(SetPatch));
			foreach(var code in instructions) {
				yield return code;
				if(code.Calls(getValue)) {
					yield return new CodeInstruction(OpCodes.Dup);
					yield return new CodeInstruction(OpCodes.Call, setValue);
				}
			}
		}

		internal static void SetPatch(LoadableXmlAsset asset) {
			Utility.CurrentAsset.Value = asset;
		}
	}

	[HarmonyPatch]
	internal static class System__Xml__XmlNodeList__Item {

		internal static bool Prepare() => LogAfterDefErrorModSettings.PatchTraceEnabled;

		internal static IEnumerable<MethodBase> TargetMethods() => AppDomain.CurrentDomain
			.GetAssemblies()
			.SelectMany(x => x.GetTypes())
			.Where(x => x.IsSubclassOf(typeof(XmlNodeList)))
			.Where(x => x.Method(nameof(XmlNodeList.Item), [typeof(int)]).DeclaringType == x)
			.Select(x => x.Method(nameof(XmlNodeList.Item), [typeof(int)]));

		internal static void Postfix(XmlNode __result) {
			if(__result == null || Utility.PatchTracer.Value.Count == 0) return;
			var node = __result;
			while(node != null && node.ParentNode != null && node.ParentNode.Name != "Defs")
				node = node.ParentNode;
			if(node.ParentNode?.Name == "Defs") {
				if(Utility.Operations.TryGetValue(node, out var value)) value.Add(Utility.PatchTracer.Value.Peek());
				else Utility.Operations[node] = [Utility.PatchTracer.Value.Peek()];
			}
		}

	}

	[HarmonyPatch(typeof(PatchOperation), nameof(PatchOperation.Apply))]
	internal static class Verse__PatchOperation__Apply {

		internal static bool Prepare() => LogAfterDefErrorModSettings.PatchTraceEnabled;

		internal static void Prefix(PatchOperation __instance) {
			if(!Utility.Patches.ContainsKey(__instance)
				&& Utility.PatchTracer.Value.Count > 0
				&& Utility.Patches.ContainsKey(Utility.PatchTracer.Value.Peek())) {
				Utility.Patches[__instance] = Utility.Patches[Utility.PatchTracer.Value.Peek()];
			}
			Utility.PatchTracer.Value.Push(__instance);
		}

		internal static void Postfix() => Utility.PatchTracer.Value.Pop();
	}

	internal static class RimWorld__MainMenuDrawer__MainMenuOnGUI {

		internal static void Postfix() {
			if(LogAfterDefErrorMod.hasExpection)
				Find.WindowStack.Add(new Dialog_MessageBox("LogAfterDefError.Exception.Message".Translate(), "Confirm".Translate(), 
					() => LogAfterDefErrorMod.hasExpection = false));
		}

		internal static void Patch(Harmony instance) {
			instance.Patch(typeof(MainMenuDrawer).Method(nameof(MainMenuDrawer.MainMenuOnGUI)), postfix: new HarmonyMethod(typeof(RimWorld__MainMenuDrawer__MainMenuOnGUI), nameof(Postfix)));
		}
	}
}
