using System.IO;
using UnityEditor;
using UnityEngine;

public class StorageEditor : EditorWindow
{
    private string _fileName;
    private string _fileContent;
    private Vector2 _scrollPosition;

    [MenuItem("Storage/Open")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<StorageEditor>().Show();
    }
    
    public void OnGUI()
    {
        if (GUILayout.Button("Open"))
        {
            var fileExists = false;
            var menu = new GenericMenu();
            
            foreach (var file in Directory.EnumerateFiles(Storage.StorageDirectory, "*.json"))
            {
                fileExists = true;
                var fileName = Path.GetFileName(file);
                menu.AddItem(new GUIContent(fileName), fileName == _fileName, OnSelectFileName, file);
            }
            
            if (!fileExists)
            {
                menu.AddItem(new GUIContent("Directory is empty"), false, () => { });
            }
            menu.ShowAsContext();
        }
        
        if (!string.IsNullOrEmpty(_fileContent))
        {
            GUILayout.Label("File: " + _fileName);
            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
            GUILayout.TextArea(_fileContent);
            GUILayout.EndScrollView();
        }
        else
        {
            GUILayout.Box("Choose another file.");
        }
    }

    public void OnSelectFileName(object file)
    {
        _fileName = Path.GetFileName(file as string);
        _fileContent = File.ReadAllText(file as string);
    }
}