﻿using System.IO;
using UnityEditor;
using UnityEngine;
#if UNITY_IOS
using System.Collections.Generic;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
#endif

namespace NativeFilePickerNamespace
{
	public class NativeFilePickerPostProcessBuild
	{
		private const bool ENABLED = false;

		[InitializeOnLoadMethod]
		public static void ValidatePlugin()
		{
			string jarPath = "Assets/Plugins/NativeFilePicker/Android/NativeFilePicker.jar";
			if( File.Exists( jarPath ) )
			{
				Debug.Log( "Deleting obsolete " + jarPath );
				AssetDatabase.DeleteAsset( jarPath );
			}
		}

#if UNITY_IOS
#if !UNITY_2017_1_OR_NEWER
		private const string ICLOUD_ENTITLEMENTS = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
			"<!DOCTYPE plist PUBLIC \" -//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">" +
			"<plist version=\"1.0\">" +
			"<dict>" +
			"<key>com.apple.developer.icloud-container-identifiers</key>" +
			"<array>" +
			"<string>iCloud.$(CFBundleIdentifier)</string>" +
			"</array>" +
			"<key>com.apple.developer.icloud-services</key>" +
			"<array>" +
			"<string>CloudDocuments</string>" +
			"</array>" +
			"<key>com.apple.developer.ubiquity-container-identifiers</key>" +
			"<array>" +
			"<string>iCloud.$(CFBundleIdentifier)</string>" +
			"</array>" +
			"</dict>" +
			"</plist>";
#endif

#pragma warning disable 0162
		[PostProcessBuild]
		public static void OnPostprocessBuild( BuildTarget target, string buildPath )
		{
			// Add declared custom types to Info.plist
			if( target == BuildTarget.iOS )
			{
				NativeFilePickerCustomTypes.TypeHolder[] customTypes = NativeFilePickerCustomTypes.GetCustomTypes();
				if( customTypes != null )
				{
					string plistPath = Path.Combine( buildPath, "Info.plist" );

					PlistDocument plist = new PlistDocument();
					plist.ReadFromString( File.ReadAllText( plistPath ) );

					PlistElementDict rootDict = plist.root;

					for( int i = 0; i < customTypes.Length; i++ )
					{
						NativeFilePickerCustomTypes.TypeHolder customType = customTypes[i];
						PlistElementArray customTypesArray = GetCustomTypesArray( rootDict, customType.isExported );

						// Don't allow duplicate entries
						RemoveCustomTypeIfExists( customTypesArray, customType.identifier );

						PlistElementDict customTypeDict = customTypesArray.AddDict();
						customTypeDict.SetString( "UTTypeIdentifier", customType.identifier );
						customTypeDict.SetString( "UTTypeDescription", customType.description );

						PlistElementArray conformsTo = customTypeDict.CreateArray( "UTTypeConformsTo" );
						for( int j = 0; j < customType.conformsTo.Length; j++ )
							conformsTo.AddString( customType.conformsTo[j] );

						PlistElementDict tagSpecification = customTypeDict.CreateDict( "UTTypeTagSpecification" );
						PlistElementArray tagExtensions = tagSpecification.CreateArray( "public.filename-extension" );
						for( int j = 0; j < customType.extensions.Length; j++ )
							tagExtensions.AddString( customType.extensions[j] );
					}

					File.WriteAllText( plistPath, plist.WriteToString() );
				}
			}

			// Rest of the function shouldn't execute unless build post-processing is ENABLED
			if( !ENABLED )
				return;

			if( target == BuildTarget.iOS )
			{
				string pbxProjectPath = PBXProject.GetPBXProjectPath( buildPath );

				PBXProject pbxProject = new PBXProject();
				pbxProject.ReadFromFile( pbxProjectPath );

#if UNITY_2019_3_OR_NEWER
				string targetGUID = pbxProject.GetUnityFrameworkTargetGuid();
#else
				string targetGUID = pbxProject.TargetGuidByName( PBXProject.GetUnityTargetName() );
#endif

				pbxProject.AddBuildProperty( targetGUID, "OTHER_LDFLAGS", "-framework MobileCoreServices" );
				pbxProject.AddBuildProperty( targetGUID, "OTHER_LDFLAGS", "-framework CloudKit" );

#if !UNITY_2017_1_OR_NEWER
				// Add iCloud capability (Cloud Build isn't supported)
				string entitlementsPath = Path.Combine( buildPath, "iCloud.entitlements" );
				File.WriteAllText( entitlementsPath, ICLOUD_ENTITLEMENTS );
				pbxProject.AddFile( entitlementsPath, Path.GetFileName( entitlementsPath ) );
				pbxProject.AddBuildProperty( targetGUID, "CODE_SIGN_ENTITLEMENTS", entitlementsPath );
#endif
				
				File.WriteAllText( pbxProjectPath, pbxProject.WriteToString() );

#if UNITY_2017_1_OR_NEWER
				// Add iCloud capability with Cloud Build support on 2017.1+
#if UNITY_2019_3_OR_NEWER
				ProjectCapabilityManager manager = new ProjectCapabilityManager( pbxProjectPath, "iCloud.entitlements", "Unity-iPhone" );
#else
				ProjectCapabilityManager manager = new ProjectCapabilityManager( pbxProjectPath, "iCloud.entitlements", PBXProject.GetUnityTargetName() );
#endif
#if UNITY_2018_3_OR_NEWER
				manager.AddiCloud( false, true, false, true, null );
#else
				manager.AddiCloud( false, true, true, null );
#endif
				manager.WriteToFile();
#endif
			}
		}

		// Adding PRODUCT_BUNDLE_IDENTIFIER if not exists (if another plugin also fills this value, we must not touch it)
		[PostProcessBuild( 99 )]
		public static void OnPostprocessBuild2( BuildTarget target, string buildPath )
		{
			if( !ENABLED )
				return;

			if( target == BuildTarget.iOS )
			{
				string pbxProjectPath = PBXProject.GetPBXProjectPath( buildPath );

				PBXProject pbxProject = new PBXProject();
				pbxProject.ReadFromFile( pbxProjectPath );

#if UNITY_2019_3_OR_NEWER
				string targetGUID = pbxProject.GetUnityFrameworkTargetGuid();
#else
				string targetGUID = pbxProject.TargetGuidByName( PBXProject.GetUnityTargetName() );
#endif

#if UNITY_2018_2_OR_NEWER
				if( string.IsNullOrEmpty( pbxProject.GetBuildPropertyForAnyConfig( targetGUID, "PRODUCT_BUNDLE_IDENTIFIER" ) ) )
					pbxProject.AddBuildProperty( targetGUID, "PRODUCT_BUNDLE_IDENTIFIER", PlayerSettings.applicationIdentifier );
#else
				pbxProject.SetBuildProperty( targetGUID, "PRODUCT_BUNDLE_IDENTIFIER", PlayerSettings.applicationIdentifier );
#endif

				File.WriteAllText( pbxProjectPath, pbxProject.WriteToString() );
			}
		}

		private static PlistElementArray GetCustomTypesArray( PlistElementDict rootDict, bool isExported )
		{
			string key = isExported ? "UTExportedTypeDeclarations" : "UTImportedTypeDeclarations";
			PlistElementArray result = rootDict[key] as PlistElementArray;
			if( result == null )
				result = rootDict.CreateArray( key );

			return result;
		}

		private static void RemoveCustomTypeIfExists( PlistElementArray customTypesArray, string UTI )
		{
			List<PlistElement> values = customTypesArray.values;
			if( values == null )
				return;

			for( int i = values.Count - 1; i >= 0; i-- )
			{
				PlistElementDict exportedType = values[i] as PlistElementDict;
				if( exportedType != null )
				{
					PlistElementString exportedTypeID = exportedType["UTTypeIdentifier"] as PlistElementString;
					if( exportedTypeID != null && exportedTypeID.value == UTI )
						values.RemoveAt( i );
				}
			}
		}
#pragma warning restore 0162
#endif
	}
}