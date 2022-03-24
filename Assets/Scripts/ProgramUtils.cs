using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class ProgramUtils
{
    public static Exception DependencyException(List<MonoBehaviour> deps, List<Type> depTypes)
    {
        if (deps.Count != depTypes.Count)
        {
            throw new Exception("List of dependencies and list of respective dependency types must have equal length");
        }

        if (deps.Count == 0)
        {
            throw new Exception(
                "Expected list of dependencies, got empty list (dude I can't make u a dependency exception without dependencies)");
        }

        string text = string.Format("Expected {0} dependencies, missing ", deps.Count);
        for (int i = 0; i < deps.Count; i++)
        {
            if (deps[i] == null)
            {
                text = text + depTypes[i] + ", ";
            }
        }

        return new Exception(text);
    }

    private static Dictionary<string, GameObject> stringToGameObject = new Dictionary<string, GameObject>();

    /// <summary>
    /// Finds the item with the given name. 
    /// If this GameObject.Find(name) call has been executed before, then return the cached gameobject.
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public static GameObject FindUnique(string name)
    {
        if (!stringToGameObject.ContainsKey(name) || stringToGameObject[name] == null)
        {
            stringToGameObject[name] = GameObject.Find(name);
            return stringToGameObject[name];
        }
        else
        {
            return stringToGameObject[name];
        }
    }

    public static Exception MissingComponentException(Type component)
    {
        string text = string.Format("Missing component {0}", component);
        return new Exception(text);
    }

    private static Dictionary<Type, MonoBehaviour> typeToMonoBehaviour = new Dictionary<Type, MonoBehaviour>();

    public static T FindSingletonMonoBehaviour<T>() where T : MonoBehaviour
    {
        if (typeToMonoBehaviour.ContainsKey(typeof(T)) && typeToMonoBehaviour[typeof(T)] != null)
        {
            return typeToMonoBehaviour[typeof(T)] as T;
        }
        else
        {
            T script = GameObject.FindObjectOfType(typeof(T)) as T;
            typeToMonoBehaviour[typeof(T)] = script;
            return script;
        }
    }

    public static Exception LogicException(GameObject invoker, string message)
    {
        //GUI.Box(new Rect(invoker.transform.position.x, invoker.transform.position.y, Constants.DEBUG_BOX_WIDTH, Constants.DEBUG_BOX_HEIGHT), message);
        throw new Exception(message);
    }

    public static void PrintList<T>(List<T> list)
    {
        if (list == null || list.Count == 0)
        {
            Debug.Log($"empty");
        }

        foreach (T item in list)
        {
            Debug.Log(item);
        }
    }

    public static void PrintIEnumerable<T>(IEnumerable<T> list)
    {
        foreach (T item in list)
        {
            Debug.Log(item);
        }
    }

    public static List<Type> GetMonoBehavioursOnType(Type script)
    {
        return script.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(fieldInfo => fieldInfo.FieldType)
            .Where(type => type.IsSubclassOf(typeof(MonoBehaviour)))
            .ToList();
    }

    public static bool HasImplicitConversion(Type baseType, Type targetType)
    {
        return baseType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(mi => mi.Name == "op_Implicit" && mi.ReturnType == targetType)
            .Any(mi =>
            {
                ParameterInfo pi = mi.GetParameters().FirstOrDefault();
                return pi != null && pi.ParameterType == baseType;
            });
    }

    /// <summary>
    /// Gets the i:th parameter of the list.
    /// 
    /// Throws an exception if assumptions failed.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="parameters"></param>
    /// <param name="i"></param>
    /// <returns></returns>
    public static T GetParameter<T>(List<object> parameters, int i, bool CanBeNull = false)
    {
        if (parameters == null)
        {
            throw new Exception($"Invalid parameters, list is null");
        }

        if (parameters.Count < i + 1)
        {
            throw new Exception($"Expected list with at least {i + 1} items, found {parameters.Count}");
        }

        if (parameters[i] == null && !CanBeNull)
        {
            throw new Exception($"Expected parameter to be {typeof(T)}, found null");
        }
        else if (parameters[i] != null && parameters[i].GetType() != typeof(T) &&
                 !parameters[i].GetType().IsSubclassOf(typeof(T)))
        {
            throw new Exception(
                $"Expected parameter to be {typeof(T)} or subclass thereof, found {parameters[i].GetType()}");
        }

        return (T) parameters[i];
    }
}