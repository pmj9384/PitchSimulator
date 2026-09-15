using System;
using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour, IUIManager
{
    private readonly Dictionary<Type, UIScreen> screens = new();
    private readonly Dictionary<Type, UIPopup> popups = new();



    protected void Setup()
    {
        foreach (var screen in FindObjectsByType<UIScreen>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            screens[screen.GetType()] = screen;
            screen.SetUIManager(this);
            screen.Close();
        }

        foreach (var popup in FindObjectsByType<UIPopup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            popups[popup.GetType()] = popup;
            popup.SetUIManager(this);
            popup.Hide();
        }

        foreach (var panel in FindObjectsByType<UIPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            panel.SetUIManager(this);
        }
    }

    public void OpenScreen<T>() where T : UIScreen
    {
        foreach (var kvp in screens)
        {
            if (kvp.Key == typeof(T))
                kvp.Value.Open();
            else
                kvp.Value.Close();
        }
    }

    public void ShowPopup<T>() where T : UIPopup
    {
        if (popups.TryGetValue(typeof(T), out var popup))
            popup.Show();
    }

    public void HidePopup<T>() where T : UIPopup
    {
        if (popups.TryGetValue(typeof(T), out var popup))
            popup.Hide();
    }

    public void OpenScreen(UIScreen screen)
    {
        foreach (var entry in screens)
        {
            if (entry.Value == screen)
                entry.Value.Open();
            else
                entry.Value.Close();
        }
    }
}
