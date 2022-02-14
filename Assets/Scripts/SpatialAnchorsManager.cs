using MSebastien.Core.Singletons;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// See Unity documentation for more info: 
/// https://developer.oculus.com/experimental/spatial-anchors-persist-content-unity/
/// </summary>
public class SpatialAnchorsManager : Singleton<SpatialAnchorsManager>
{
    [SerializeField]
    public GameObject anchorPrefab;

    // Unassigned Anchor Handle default value
    public ulong invalidAnchorHandle = ulong.MaxValue;

    // Query
    // RequestID (Key) --> Spatial Anchor handle (Value)
    public Dictionary<ulong, ulong> locateAnchorRequest = new Dictionary<ulong, ulong>();

    // Query Result
    // Anchor handle (Key) --> Spatial Anchor Prefab (Value)
    public Dictionary<ulong, GameObject> resolvedAnchors = new Dictionary<ulong, GameObject>();

    private void Start()
    {
        // Bind Spatial Anchor API callbacks
        OVRManager.SpatialEntityStorageSave += OVRManager_SpatialAnchorSaved;
        OVRManager.SpatialEntityQueryResults += OVRManager_SpatialEntityQueryResults;
        OVRManager.SpatialEntityQueryComplete += OVRManager_SpatialEntityQueryComplete;
        OVRManager.SpatialEntityStorageErase += OVRManager_SpatialEntityStorageErase;
        OVRManager.SpatialEntitySetComponentEnabled += OVRManager_SpatialEntitySetComponentEnabled;
    }

    private void OnDestroy()
    {
        // Unbind Spatial Anchor API callbacks
        OVRManager.SpatialEntityStorageSave -= OVRManager_SpatialAnchorSaved;
        OVRManager.SpatialEntityQueryResults -= OVRManager_SpatialEntityQueryResults;
        OVRManager.SpatialEntityQueryComplete -= OVRManager_SpatialEntityQueryComplete;
        OVRManager.SpatialEntityStorageErase -= OVRManager_SpatialEntityStorageErase;
        OVRManager.SpatialEntitySetComponentEnabled -= OVRManager_SpatialEntitySetComponentEnabled;
    }

    private void LateUpdate()
    {
        foreach(var handleAnchorPair in resolvedAnchors)
        {
            var anchorHandle = handleAnchorPair.Key;
            var anchorObject = handleAnchorPair.Value;

            if(anchorHandle == invalidAnchorHandle)
            {
                Logger.Instance.LogError("Trying to update anchor pos and rot threw an error " +
                    $"for anchor handle {anchorHandle}");
                return;
            }

            // Set anchor gameobject transform to pose returned from LocateSpace
            var pose = OVRPlugin.LocateSpace(ref anchorHandle, OVRPlugin.GetTrackingOriginType());
            anchorObject.transform.position = pose.ToOVRPose().position;
            anchorObject.transform.rotation = pose.ToOVRPose().orientation;
        }
    }

    //-------------------- ASYNCHRONOUS RESPONSES TO REQUESTS ------------------------
    private void OVRManager_SpatialAnchorSaved(ulong requestId, ulong anchorHandle, bool result, 
        OVRPlugin.SpatialEntityUuid uuid)
    {
        Logger.Instance.LogInfo($"SpatialAnchorSaved requestId: {requestId} handle: {anchorHandle} result: {result} uuid: {GetUuidString(uuid)}");
        /*
        // Write UUID of saved anchor to "player preferences" file
        if (!PlayerPrefs.HasKey("numAnchorUuids"))
        {
            PlayerPrefs.SetInt("numAnchorUuids", 0);
        }

        // Retrieve the number of anchor handles
        int playerNumAnchorUuids = PlayerPrefs.GetInt("numAnchorUuids");

        PlayerPrefs.SetString("anchorUuid" + playerNumAnchorUuids, GetUuidString(uuid));
        PlayerPrefs.SetInt("numAnchorUuids", ++playerNumAnchorUuids);*/
    }

    private void OVRManager_SpatialEntityQueryResults(ulong requestId, int numResults, 
        OVRPlugin.SpatialEntityQueryResult[] results)
    {
        Logger.Instance.LogInfo($"SpatialEntityQueryResult requestId: {requestId} numResults: {numResults}");
        foreach (var spatialQueryResult in results)
        {
            TryEnableComponent(spatialQueryResult.space, OVRPlugin.SpatialEntityComponentType.Locatable);
            TryEnableComponent(spatialQueryResult.space, OVRPlugin.SpatialEntityComponentType.Storable);
        }
    }

    private void OVRManager_SpatialEntityQueryComplete(ulong requestId, bool result, int numFound)
    {
        Logger.Instance.LogInfo($"SpatialEntityQueryComplete requestId: {requestId} result: {result} numFound: {numFound}");
    }

    private void OVRManager_SpatialEntityStorageErase(ulong requestId, bool result, 
        OVRPlugin.SpatialEntityUuid uuid, OVRPlugin.SpatialEntityStorageLocation location)
    {
        Logger.Instance.LogInfo($"SpatialEntityStorageErase requestId: {requestId} result: {result} uuid: {GetUuidString(uuid)} location: {location}");
    }

    private void OVRManager_SpatialEntitySetComponentEnabled(ulong requestId, bool result, 
        OVRPlugin.SpatialEntityComponentType type, ulong anchorHandle)
    {
        // If the Locatable was enabled and no gameobject created
        if(locateAnchorRequest.ContainsKey(requestId) && !resolvedAnchors.ContainsKey(locateAnchorRequest[requestId]))
        {
            CreateAnchorGameObject(locateAnchorRequest[requestId]);
        }
    }
    //-------------------- END ASYNCHRONOUS OPERATIONS ------------------------

    private void CreateAnchorGameObject(ulong anchorHandle)
    {
        // We assume there is only one prefab
        GameObject anchorObject = Instantiate(anchorPrefab);
        anchorObject.GetComponentInChildren<TextMeshProUGUI>().text = $"{anchorHandle}";

        // Add GameObject to dictionary so it can be tracked
        resolvedAnchors.Add(anchorHandle, anchorObject);
    }

    /// <summary>
    /// Convert a Spatial Anchor UUID into a string
    /// </summary>
    /// <param name="uuid">Spatial Anchor UUID</param>
    /// <returns>String representation of the UUID</returns>
    private string GetUuidString(OVRPlugin.SpatialEntityUuid uuid)
    {
        byte[] uuidData = new byte[16];
        Array.Copy(BitConverter.GetBytes(uuid.Value_0), uuidData, 8);
        Array.Copy(BitConverter.GetBytes(uuid.Value_1), 0, uuidData, 8, 8);
        return AnchorHelpers.UuidToString(uuidData);
    }

    #region CRUD Operations
    /// <summary>
    /// Create a spatial anchor (aka a persistant 6DOF tracked transform in world space)
    /// </summary>
    /// <param name="transform">Transform which acts as a base for local coordinates</param>
    /// <returns>The Anchor handle</returns>
    public ulong CreateSpatialAnchor(Transform transform)
    {
        OVRPlugin.SpatialEntityAnchorCreateInfo createInfo = new OVRPlugin.SpatialEntityAnchorCreateInfo()
        {
            Time = OVRPlugin.GetTimeInSeconds(),
            BaseTracking = OVRPlugin.GetTrackingOriginType(), // Eye-Level, Floor, etc...
            PoseInSpace = OVRExtensions.ToOVRPose(transform, false).ToPosef()
        };

        ulong anchorHandle = invalidAnchorHandle;
        if(OVRPlugin.SpatialEntityCreateSpatialAnchor(createInfo, ref anchorHandle))
        {
            Logger.Instance.LogInfo($"Spatial Anchor (Handle:{anchorHandle}) has successfully been created.");
        }
        else
        {
            Logger.Instance.LogError("Failed to create a spatial anchor.");
        }

        // Try enabling the Anchor's "Locatable" component
        TryEnableComponent(anchorHandle, OVRPlugin.SpatialEntityComponentType.Locatable);
        // Try enabling the Anchor's "Storable" component to make it persistent
        TryEnableComponent(anchorHandle, OVRPlugin.SpatialEntityComponentType.Storable);

        return anchorHandle;
    }

    /// <summary>
    /// Save a Spatial Anchor to a file located in the local storage.
    /// </summary>
    /// <param name="anchorHandle">Spatial Anchor Handle</param>
    /// <param name="storageLocation">Location</param>
    public void SaveSpatialAnchor(ulong anchorHandle, OVRPlugin.SpatialEntityStorageLocation storageLocation)
    {
        ulong saveRequestId = 0;
        if (OVRPlugin.SpatialEntitySaveSpatialEntity(ref anchorHandle, storageLocation,
            OVRPlugin.SpatialEntityStoragePersistenceMode.IndefiniteHighPri, ref saveRequestId))
        {
            Logger.Instance.LogInfo($"The Spatial Anchor (Handle: {anchorHandle}) has been saved" +
                $" to local storage. (request: {saveRequestId})");
        }
        else
        {
            Logger.Instance.LogInfo($"Failed to save the Spatial Anchor (Handle: {anchorHandle})" +
                $" to local storage. (request: {saveRequestId})");
        }
    }

    public void QuerySavedPersistentSpatialAnchors()
    {
        // Get total number of saved anchor uuids
        if (!PlayerPrefs.HasKey("numAnchorUuids"))
        {
            PlayerPrefs.SetInt("numAnchorUuids", 0);
        }
        int playerNumAnchorUuids = PlayerPrefs.GetInt("numAnchorUuids");

        // Initialize a new array of Anchor UUIDs
        OVRPlugin.SpatialEntityUuid[] uuidArr = new OVRPlugin.SpatialEntityUuid[playerNumAnchorUuids];
        
        // Fill the array with all the saved Spatial Anchor UUIDs
        for(int i = 0; i < playerNumAnchorUuids; ++i)
        {
            string uuidKey = "anchorUuid" + i;
            string currentUuid = PlayerPrefs.GetString(uuidKey);

            byte[] byteArray = AnchorHelpers.StringToUuid(currentUuid);
            uuidArr[i] = new OVRPlugin.SpatialEntityUuid
            {
                Value_0 = BitConverter.ToUInt64(byteArray, 0),
                Value_1 = BitConverter.ToUInt64(byteArray, 8)
            };
        }

        // Prepare Query Info (Filter by Ids)
        var uuidInfo = new OVRPlugin.SpatialEntityFilterInfoIds
        {
            NumIds = playerNumAnchorUuids,
            Ids = uuidArr
        };

        var queryInfo = new OVRPlugin.SpatialEntityQueryInfo
        {
            QueryType = OVRPlugin.SpatialEntityQueryType.Action,
            MaxQuerySpaces = 20,
            Timeout = 0,
            Location = OVRPlugin.SpatialEntityStorageLocation.Local,
            ActionType = OVRPlugin.SpatialEntityQueryActionType.Load,
            FilterType = OVRPlugin.SpatialEntityQueryFilterType.Ids,
            IdInfo = uuidInfo
        };

        // Query Spatial Anchors (handles), thanks to UUIDs
        ulong newReqId = 0;
        if(OVRPlugin.SpatialEntityQuerySpatialEntity(queryInfo, ref newReqId))
        {
            Logger.Instance.LogInfo("Query Saved Anchors: success");
        }
        else
        {
            Logger.Instance.LogError("Query Saved Anchors: fail");
        }
    }

    public void QueryAllLocalAnchors()
    {
        var queryInfo = new OVRPlugin.SpatialEntityQueryInfo
        {
            QueryType = OVRPlugin.SpatialEntityQueryType.Action,
            MaxQuerySpaces = 20,
            Timeout = 0,
            Location = OVRPlugin.SpatialEntityStorageLocation.Local,
            ActionType = OVRPlugin.SpatialEntityQueryActionType.Load,
            FilterType = OVRPlugin.SpatialEntityQueryFilterType.None
        };

        // Query local Spatial Anchors (handles)
        ulong newReqId = 0;
        if (OVRPlugin.SpatialEntityQuerySpatialEntity(queryInfo, ref newReqId))
        {
            Logger.Instance.LogInfo("Query All Local Anchors: success");
        }
        else
        {
            Logger.Instance.LogError("Query All Local Anchors: fail");
        }
    }

    public void DestroyAnchor(ulong anchorHandle)
    {
        // Destroy anchor gameobject
        if(resolvedAnchors.ContainsKey(anchorHandle))
        {
            Destroy(resolvedAnchors[anchorHandle].gameObject);
            resolvedAnchors.Remove(anchorHandle);
        }

        // Destroy in memory
        if(!OVRPlugin.DestroySpace(ref anchorHandle))
        {
            Logger.Instance.LogError($"DestroySpace failed for anchorHandle: {anchorHandle}");
        }
    }

    public void EraseAnchor(ulong anchorHandle)
    {
        // Destroy anchor gameobject
        if(resolvedAnchors.ContainsKey(anchorHandle))
        {
            Destroy(resolvedAnchors[anchorHandle].gameObject);
            resolvedAnchors.Remove(anchorHandle);
        }

        // Erase anchor from storage
        ulong eraseRequestId = 0;
        if(!OVRPlugin.SpatialEntityEraseSpatialEntity(ref anchorHandle, 
            OVRPlugin.SpatialEntityStorageLocation.Local, ref eraseRequestId))
        {
            Logger.Instance.LogError($"SpatialEntityEraseSpatialEntity failed for anchorHandle: {anchorHandle}");
        }
    }
    #endregion

    /// <summary>
    /// Try to enable a component (Locatable or Storable) of a spatial anchor.
    /// </summary>
    /// <param name="anchorHandle"></param>
    /// <param name="componentType"></param>
    private void TryEnableComponent(ulong anchorHandle, OVRPlugin.SpatialEntityComponentType componentType)
    {
        bool success = OVRPlugin.SpatialEntityGetComponentEnabled(ref anchorHandle, componentType, 
            out bool enabled, out bool changePending);

        if(!success)
        {
            Logger.Instance.LogInfo($"TryEnableComponent on anchor handle {anchorHandle} failed.");
        }

        if (enabled)
        {
            Logger.Instance.LogInfo($"Component on anchor handle {anchorHandle} was already enabled.");
        }
        else
        {
            ulong requestId = 0;
            if (OVRPlugin.SpatialEntitySetComponentEnabled(ref anchorHandle, componentType, true, 0.0,
                ref requestId))
            {
                Logger.Instance.LogInfo($"The {componentType.ToString()} component of the Spatial Anchor " +
                    $"(Handle: {anchorHandle}) is now enabled.");
            }
            else
            {
                Logger.Instance.LogError($"Failed to enable the {componentType.ToString()} component of " +
                    $"the Spatial Anchor (Handle: {anchorHandle}).");
            }

            switch(componentType)
            { 
                case OVRPlugin.SpatialEntityComponentType.Locatable:
                    locateAnchorRequest.Add(requestId, anchorHandle);
                    break;
                case OVRPlugin.SpatialEntityComponentType.Storable:
                    break;
                default:
                    Logger.Instance.LogError($"Tried to enable an unsupported component.");
                    break;
            }
        }
    }
    
}
