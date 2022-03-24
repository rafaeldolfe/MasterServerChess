using System;
using System.Collections.Generic;
using UnityEngine;

public enum EventName
{
}

public class GlobalEventManager : MonoBehaviour
{
    private readonly Dictionary<string, Action<GameObject, List<object>, int, int, int, int>> eventDictionary =
        new Dictionary<string, Action<GameObject, List<object>, int, int, int, int>>();

    private readonly Dictionary<string, Action<GameObject, List<object>>> simpleEventDictionary =
        new Dictionary<string, Action<GameObject, List<object>>>();

    private readonly Dictionary<EventName, Action<GameObject, List<object>>> simpleEnumEventDictionary =
        new Dictionary<EventName, Action<GameObject, List<object>>>();

    private readonly Dictionary<string, Action> simplestEventDictionary = new Dictionary<string, Action>();
    private readonly Dictionary<EventName, Action> simplestEnumEventDictionary = new Dictionary<EventName, Action>();

    public void StartListening(string eventName, Action<GameObject, List<object>, int, int, int, int> listener)
    {
        // Check if it already exists in the other dictionary, to prevent accidental mis-listening
        if (simpleEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (eventDictionary.ContainsKey(eventName))
        {
            eventDictionary[eventName] += listener;
        }
        else
        {
            eventDictionary.Add(eventName, listener);
        }
    }

    public void StartListening(string eventName, Action<GameObject, List<object>> listener)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simpleEventDictionary.ContainsKey(eventName))
        {
            simpleEventDictionary[eventName] += listener;
        }
        else
        {
            simpleEventDictionary.Add(eventName, listener);
        }
    }

    public void StartListening(EventName eventName, Action<GameObject, List<object>> listener)
    {
        if (simplestEnumEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simpleEnumEventDictionary.ContainsKey(eventName))
        {
            simpleEnumEventDictionary[eventName] += listener;
        }
        else
        {
            simpleEnumEventDictionary.Add(eventName, listener);
        }
    }

    public void StartListening(string eventName, Action listener)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simpleEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEventDictionary.ContainsKey(eventName))
        {
            simplestEventDictionary[eventName] += listener;
        }
        else
        {
            simplestEventDictionary.Add(eventName, listener);
        }
    }

    public void StartListening(EventName eventName, Action listener)
    {
        if (simpleEnumEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEnumEventDictionary.ContainsKey(eventName))
        {
            simplestEnumEventDictionary[eventName] += listener;
        }
        else
        {
            simplestEnumEventDictionary.Add(eventName, listener);
        }
    }

    public void StopListening(string eventName, Action<GameObject, List<object>, int, int, int, int> listener)
    {
        if (simpleEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                "Duplicate event in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                "Duplicate event in dictionary with other parameters, recommended to use unique event names");
        }

        if (eventDictionary.ContainsKey(eventName))
        {
            eventDictionary[eventName] -= listener;
            if (eventDictionary[eventName] == null)
            {
                eventDictionary.Remove(eventName);
            }
        }
    }

    public void StopListening(string eventName, Action<GameObject, List<object>> listener)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                "Duplicate event in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                "Duplicate event in dictionary with other parameters, recommended to use unique event names");
        }

        if (simpleEventDictionary.ContainsKey(eventName))
        {
            simpleEventDictionary[eventName] -= listener;
            if (simpleEventDictionary[eventName] == null)
            {
                simpleEventDictionary.Remove(eventName);
            }
        }
    }

    public void StopListening(EventName eventName, Action<GameObject, List<object>> listener)
    {
        if (simplestEnumEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                "Duplicate event in dictionary with other parameters, recommended to use unique event names");
        }

        if (simpleEnumEventDictionary.ContainsKey(eventName))
        {
            simpleEnumEventDictionary[eventName] -= listener;
            if (simpleEnumEventDictionary[eventName] == null)
            {
                simpleEnumEventDictionary.Remove(eventName);
            }
        }
    }

    public void StopListening(string eventName, Action listener)
    {
        if (eventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simpleEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEventDictionary.ContainsKey(eventName))
        {
            simplestEventDictionary[eventName] -= listener;
            if (simplestEventDictionary[eventName] == null)
            {
                simplestEventDictionary.Remove(eventName);
            }
        }
    }

    public void StopListening(EventName eventName, Action listener)
    {
        if (simpleEnumEventDictionary.ContainsKey(eventName))
        {
            Debug.LogWarning(
                $"Duplicate event {eventName} in dictionary with other parameters, recommended to use unique event names");
        }

        if (simplestEnumEventDictionary.ContainsKey(eventName))
        {
            simplestEnumEventDictionary[eventName] -= listener;
            if (simplestEnumEventDictionary[eventName] == null)
            {
                simplestEnumEventDictionary.Remove(eventName);
            }
        }
    }

    public void TriggerEvent(string eventName, GameObject invoker = null, List<object> parameters = null, int x = -1,
        int y = -1, int tx = -1, int ty = -1)
    {
        parameters = parameters == null ? new List<object>() : parameters;
        Action<GameObject, List<object>, int, int, int, int> thisEvent;
        Action<GameObject, List<object>> thisSimpleEvent;
        Action thisSimplestEvent;
        if (eventDictionary.TryGetValue(eventName, out thisEvent))
        {
            thisEvent.Invoke(invoker, parameters, x, y, tx, ty);
        }
        else if (simpleEventDictionary.TryGetValue(eventName, out thisSimpleEvent))
        {
            thisSimpleEvent.Invoke(invoker, parameters);
        }
        else if (simplestEventDictionary.TryGetValue(eventName, out thisSimplestEvent))
        {
            thisSimplestEvent.Invoke();
        }
    }

    public void TriggerEvent(EventName eventName, GameObject invoker = null, List<object> parameters = null, int x = -1,
        int y = -1, int tx = -1, int ty = -1)
    {
        parameters = parameters == null ? new List<object>() : parameters;
        Action<GameObject, List<object>> thisSimpleEvent;
        Action thisSimplestEvent;
        if (simpleEnumEventDictionary.TryGetValue(eventName, out thisSimpleEvent))
        {
            thisSimpleEvent.Invoke(invoker, parameters);
        }
        else if (simplestEnumEventDictionary.TryGetValue(eventName, out thisSimplestEvent))
        {
            thisSimplestEvent.Invoke();
        }
    }
}