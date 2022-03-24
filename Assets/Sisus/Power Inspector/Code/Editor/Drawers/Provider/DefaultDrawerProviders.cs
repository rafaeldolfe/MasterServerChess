#define CACHE_TO_DISK

//#define DEBUG_SETUP

using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;
using JetBrains.Annotations;
using Sisus.Attributes;
using Sisus.Compatibility;
using UnityEngine;

namespace Sisus
{
	/// <summary>
	/// Class that handles creating, caching and returning default drawer providers for inspectors.
	/// </summary>
	[InitializeOnLoad]
	public static class DefaultDrawerProviders
	{
		private static Dictionary<Type, IDrawerProvider> drawerProvidersByInspectorType = new Dictionary<Type, IDrawerProvider>(2);

		private static bool isReady;
		private static bool selfReady;

		private volatile static bool threadedFullRebuildFinished = false;
		private static bool threadedFullRebuildApplied = false;
		private volatile static ConcurrentDictionary<Type, IDrawerProvider> drawerProvidersByInspectorTypeRebuilt;

		public static bool IsReady
		{
			get
			{
				if(!isReady)
				{
					if(!selfReady)
					{
						return false;
					}

					foreach(var provider in drawerProvidersByInspectorType.Values)
					{
						if(!provider.IsReady)
						{
							#if DEV_MODE
							UnityEngine.Debug.LogWarning("DefaultDrawerProviders.IsReady false because provider "+provider.GetType().Name+".IsReady=false");
							#endif
							return false;
						}
					}
					isReady = true;
				}

				return true;
			}
		}

		static DefaultDrawerProviders()
		{
			#if CACHE_TO_DISK && (!NET_2_0 && !NET_2_0_SUBSET && !NET_STANDARD_2_0) // HashSet.GetObjectData is not implemented in older versions
			if(System.IO.File.Exists(SavePath()))
			{
				Deserialize();
			}
			#endif

			// Delay is needed because can't access asset database to load InspectorPreferences asset from constructor.
			EditorApplication.delayCall -= Setup;
			EditorApplication.delayCall += Setup;
		}

		#if CACHE_TO_DISK && (!NET_2_0 && !NET_2_0_SUBSET && !NET_STANDARD_2_0) // HashSet.GetObjectData is not implemented in older versions
		public static void Deserialize()
		{
			var cachePath = SavePath();
			if(!System.IO.File.Exists(cachePath))
			{
				return;
			}

			isReady = false;
			selfReady = false;

			try
			{
				var bytes = System.IO.File.ReadAllBytes(cachePath);
				using (var memStream = new System.IO.MemoryStream())
				{
					var binForm = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
					memStream.Write(bytes, 0, bytes.Length);
					memStream.Seek(0, System.IO.SeekOrigin.Begin);
					drawerProvidersByInspectorType = (Dictionary<Type, IDrawerProvider>)binForm.Deserialize(memStream);
				}

				foreach(var deserializedItem in drawerProvidersByInspectorType)
				{
					if(deserializedItem.Key == null || deserializedItem.Value == null)
					{
						drawerProvidersByInspectorType.Clear();
						return;
					}
				}

				#if DEV_MODE && PI_ASSERTATIONS
				UnityEngine.Debug.Assert(drawerProvidersByInspectorType != null, "Drawer providers dictionary was null");
				IDrawerProvider found;
				UnityEngine.Debug.Assert(drawerProvidersByInspectorType.TryGetValue(typeof(PowerInspector), out found), "Drawer provider for Power Inspector not found after deserialize");
				foreach(var drawerProvider in drawerProvidersByInspectorType.Values)
				{
					UnityEngine.Debug.Assert(drawerProvider.IsReady, drawerProvider.GetType()+ ".IsReady was false after deserialize.");
				}
				#endif

				if(!drawerProvidersByInspectorType.ContainsKey(typeof(PowerInspector)) || !drawerProvidersByInspectorType[typeof(PowerInspector)].DrawerProviderData.ValidateData())
				{
					drawerProvidersByInspectorType.Clear();
					return;
				}

				foreach(var drawerProvider in drawerProvidersByInspectorType.Values)
                {
					drawerProvider.UsingDeserializedDrawers = true;
				}

				isReady = true;
				selfReady = true;

				#if DEV_MODE && PI_ASSERTATIONS
				UnityEngine.Debug.Assert(IsReady, "DefaultDrawerProviders.IsReady was false");
				#endif
			}
			#if DEV_MODE
			catch(Exception e)
			{
				UnityEngine.Debug.LogWarning(e);
			#else
			catch
			{
			#endif
				if(drawerProvidersByInspectorType == null)
				{
					drawerProvidersByInspectorType = new Dictionary<Type, IDrawerProvider>(2);
				}
			}
		}

		private static string SavePath()
		{
			return System.IO.Path.Combine(UnityEngine.Application.temporaryCachePath, "PowerInspector.DefaultDrawerProviders.data");
		}

		public static void Serialize()
		{
			using(var stream = new System.IO.MemoryStream())
			{
				var formatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
				formatter.Serialize(stream, drawerProvidersByInspectorType);
				System.IO.File.WriteAllBytes(SavePath(), stream.ToArray());
			}
		}
		#endif

		private static void Setup()
		{
			// Wait until Inspector contents have been rebuilt using deserialized cached drawers until moving on to fully rebuilding drawers from scratch.
			// This is because the process of building all the drawers can take a couple of seconds, and we don't want to keep the user waiting for this duration.
			// If isReady is false then no existing state was deserialized before Setup was called, and we can skip this part.
			if(isReady)
			{
				foreach(var inspector in InspectorManager.Instance().ActiveInstances)
				{
					if(!inspector.SetupDone)
					{
						#if DEV_MODE && DEBUG_SETUP
						UnityEngine.Debug.Log("DefaultDrawerProviders - waiting until inspector Setup Done...");
						#endif
						EditorApplication.delayCall -= Setup;
						EditorApplication.delayCall += Setup;
						return;
					}
				}
				#if DEV_MODE && DEBUG_SETUP
				UnityEngine.Debug.Log("Setup now done for all "+ InspectorManager.Instance().ActiveInstances.Count+" inspectors");
				#endif
			}

			// Make sure that Preferences have been fetched via AssetDatabase.LoadAssetAtPath before moving on to threaded code
			var preferences = InspectorUtility.Preferences;
			UnityEngine.Debug.Assert(preferences != null, "Preferences null");

			SetupThreaded();

			ApplyRebuiltSetupDataWhenReady();
		}

		private static void ApplyRebuiltSetupDataWhenReady()
		{
			if(threadedFullRebuildApplied)
			{
				return;
			}

			if(!threadedFullRebuildFinished)
			{
				#if DEV_MODE && DEBUG_SETUP
				UnityEngine.Debug.Log("DefaultDrawerProviders.ApplyRebuiltSetupDataWhenReady delaying because !threadedFullRebuildFinished...");
				#endif
				EditorApplication.delayCall -= ApplyRebuiltSetupDataWhenReady;
				EditorApplication.delayCall += ApplyRebuiltSetupDataWhenReady;
				return;
			}

			foreach(var provider in drawerProvidersByInspectorTypeRebuilt.Values)
			{
				if(!provider.IsReady)
				{
					#if DEV_MODE && DEBUG_SETUP
					UnityEngine.Debug.LogWarning("DefaultDrawerProviders.ApplyRebuiltSetupDataWhenReady delaying because provider !" + provider.GetType().Name+".IsReady");
					#endif
					EditorApplication.delayCall -= ApplyRebuiltSetupDataWhenReady;
					EditorApplication.delayCall += ApplyRebuiltSetupDataWhenReady;
					return;
				}
			}

			isReady = false;
			selfReady = true;
			threadedFullRebuildApplied = true;

			#if DEV_MODE && DEBUG_SETUP
			UnityEngine.Debug.Log("DefaultDrawerProviders - Applying rebuilt setup data now!");
			#endif

			#if DEV_MODE && PI_ASSERTATIONS
			UnityEngine.Debug.Assert(drawerProvidersByInspectorTypeRebuilt.Count > 0, "drawerProvidersByInspectorTypeRebuilt.Count was 0");
			#endif

			drawerProvidersByInspectorType.Clear();
			foreach(var item in drawerProvidersByInspectorTypeRebuilt)
			{
				drawerProvidersByInspectorType.Add(item.Key, item.Value);
			}
			drawerProvidersByInspectorTypeRebuilt.Clear();

			#if ODIN_INSPECTOR
			// It takes some time for Odin inspector to inject its OdinEditor to the inspector,
			// so rebuild all open inspectors at this point so that any custom editors are using
			// Odin inspector when they should be.
			var manager = InspectorUtility.ActiveManager;
			if(manager == null)
			{
				return;
			}
			foreach(var inspector in manager.ActiveInstances)
			{
				if(Event.current == null)
                {
					inspector.OnNextLayout(inspector.ForceRebuildDrawers);
				}
				else
                {
					inspector.ForceRebuildDrawers();
				}
			}
			#endif
		}

		#if !CSHARP_7_3_OR_NEWER
		private static void SetupThreaded()
		#else
		private async static void SetupThreaded()
		#endif
		{
			#if DEV_MODE
			var timer = new ExecutionTimeLogger();
			timer.Start("DefaultDrawerProviders.SetupThreaded");
			#endif

			drawerProvidersByInspectorTypeRebuilt = new ConcurrentDictionary<Type, IDrawerProvider>();

			#if DEV_MODE
			timer.StartInterval("FindDrawerProviderForAttributesInTypes");
			#endif

			var typesToCheck = TypeExtensions.GetAllTypesThreadSafe(typeof(IDrawerProvider).Assembly, false, true, true);
			#if !CSHARP_7_3_OR_NEWER
			SetupTypesInParallel(typesToCheck);
			#else
			await SetupTypesInParallel(typesToCheck);
			#endif

			#if DEV_MODE
			timer.FinishInterval();
			timer.StartInterval("Add derived types");
			#endif

			// Also add derived types of inspector types
			var addDerived = new List<KeyValuePair<Type, IDrawerProvider>>();
			foreach(var drawerByType in drawerProvidersByInspectorTypeRebuilt)
			{
				var exactInspectorType = drawerByType.Key;
				var derivedInspectorTypes = exactInspectorType.IsInterface ? exactInspectorType.GetImplementingTypes(true, false) : exactInspectorType.GetExtendingTypes(true, false);
				foreach(var derivedInspectorType in derivedInspectorTypes)
				{
					addDerived.Add(new KeyValuePair<Type, IDrawerProvider>(derivedInspectorType, drawerByType.Value));
				}
			}

			for(int n = addDerived.Count - 1; n >= 0; n--)
			{
				var add = addDerived[n];
				var derivedInspectorType = add.Key;
				if(!drawerProvidersByInspectorTypeRebuilt.ContainsKey(derivedInspectorType))
				{
					drawerProvidersByInspectorTypeRebuilt[derivedInspectorType] = add.Value;
				}
			}

			#if DEV_MODE && PI_ASSERTATIONS
			IDrawerProvider powerInspectorDrawerProvider;
			UnityEngine.Debug.Assert(drawerProvidersByInspectorTypeRebuilt.TryGetValue(typeof(PowerInspector), out powerInspectorDrawerProvider));
			UnityEngine.Debug.Assert(powerInspectorDrawerProvider != null);
			#endif

			threadedFullRebuildFinished = true;
			threadedFullRebuildApplied = false;

			bool allReady = true;
			foreach(var drawerProvider in drawerProvidersByInspectorTypeRebuilt.Values)
			{
				if(!drawerProvider.IsReady)
				{
					allReady = false;
					drawerProvider.OnBecameReady += OnDrawerProviderBecameReady;
				}
			}

			if(allReady)
			{
				// Trigger immediate Repaint for all active inspectors, so they'll rebuild their contents asap.
				if(InspectorUtility.ActiveManager != null)
				{
					foreach(var inspector in InspectorUtility.ActiveManager.ActiveInstances)
					{
						inspector.RefreshView();
					}
				}
			}

			#if DEV_MODE
			timer.FinishInterval();
			timer.FinishAndLogResults();
			#endif

			#if CACHE_TO_DISK && (!NET_2_0 && !NET_2_0_SUBSET && !NET_STANDARD_2_0) // HashSet.GetObjectData is not implemented in older versions
			EditorApplication.delayCall -= Serialize;
			EditorApplication.delayCall += Serialize;
			#endif
		}

		private static void OnDrawerProviderBecameReady(IDrawerProvider becameReady)
		{
			becameReady.OnBecameReady -= OnDrawerProviderBecameReady;

			foreach(var drawerProvider in drawerProvidersByInspectorTypeRebuilt.Values)
			{
				if(!drawerProvider.IsReady)
				{
					return;
				}
			}

			// Trigger immediate Repaint for all active inspectors, so they'll rebuild their contents asap.
			if(InspectorUtility.ActiveManager != null)
			{
				foreach(var inspector in InspectorUtility.ActiveManager.ActiveInstances)
				{
					inspector.RefreshView();
				}
			}

			if(!threadedFullRebuildApplied)
			{
				EditorApplication.delayCall -= ApplyRebuiltSetupDataWhenReady;
				ApplyRebuiltSetupDataWhenReady();
			}
		}

		private static Task SetupTypesInParallel(IEnumerable<Type> types)
		{
			return Task.WhenAll(types.Select(CreateSetupTask));
		}

		private static Task CreateSetupTask(Type type)
		{
			return Task.Run(()=>SetupType(type));
		}

		private static void SetupType(Type type)
		{
			if(!typeof(IDrawerProvider).IsAssignableFrom(type))
			{
				return;
			}

			foreach(var drawerProviderFor in AttributeUtility.GetAttributes<DrawerProviderForAttribute>(type))
			{
				var inspectorType = drawerProviderFor.inspectorType;
				if(inspectorType == null)
				{
					UnityEngine.Debug.LogError(drawerProviderFor.GetType().Name + " on class "+type.Name+" NullReferenceException - inspectorType was null!");
					return;
				}

				IDrawerProvider drawerProvider;
				if(!drawerProvidersByInspectorTypeRebuilt.TryGetValue(inspectorType, out drawerProvider) || !drawerProviderFor.isFallback)
				{
					bool reusedExistingInstance = false;
					foreach(var createdDrawerProvider in drawerProvidersByInspectorTypeRebuilt.Values)
					{
						if(createdDrawerProvider.GetType() == type)
						{
							drawerProvidersByInspectorTypeRebuilt[inspectorType] = createdDrawerProvider;
							reusedExistingInstance = true;
							break;
						}
					}
					
					if(!reusedExistingInstance)
					{
						#if DEV_MODE && DEBUG_SETUP
						UnityEngine.Debug.Log("Creating new DrawerProvider instance of type "+type.Name+" for inspector"+inspectorType.Name);
						#endif

						object instance;
						try
						{
							instance = Activator.CreateInstance(type);
						}
						#if DEV_MODE
						catch(System.Reflection.TargetInvocationException e)
						{
							UnityEngine.Debug.LogWarning("Activator.CreateInstance(" + type.FullName + ") " + e);
						#else
						catch(System.Reflection.TargetInvocationException)
						{
						#endif
							return;
						}

						var drawerProviderInstance = (IDrawerProvider)instance;

						#if DEV_MODE && PI_ASSERTATIONS
						UnityEngine.Debug.Assert(drawerProviderInstance != null);
						#endif

						drawerProvidersByInspectorTypeRebuilt[inspectorType] = drawerProviderInstance;

						#if DEV_MODE && PI_ASSERTATIONS
						UnityEngine.Debug.Assert(drawerProvidersByInspectorTypeRebuilt[inspectorType] != null);
						#endif
					}
				}
			}
		}

		[CanBeNull]
		public static IDrawerProvider GetForInspector(Type inspectorType)
		{
			#if DEV_MODE && PI_ASSERTATIONS
			UnityEngine.Debug.Assert(IsReady);
			#endif

			IDrawerProvider drawerProvider;
			return drawerProvidersByInspectorType.TryGetValue(inspectorType, out drawerProvider) ? drawerProvider : null;
		}
	}
}