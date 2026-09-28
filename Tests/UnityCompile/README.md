# Unity assembly static compilation

This check compiles the actual `Assets` sources against the installed Unity managed assemblies. Three separate projects preserve the Core, Runtime, and Editor assembly boundaries from the Unity assembly definitions. The Core project has no Unity references.

It uses C# 9 and .NET Standard 2.1 with `UNITY_EDITOR` defined, including the editor smoke helper. The .NET SDK and its .NET Standard 2.1 reference pack must be installed. There are no NuGet package dependencies; the local NuGet configuration disables package sources.

From the repository root:

```powershell
dotnet build Tests/UnityCompile/Editor/Convergence.Editor.csproj -m:1
```

The default editor is `C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor`. To use another installation:

```powershell
dotnet build Tests/UnityCompile/Editor/Convergence.Editor.csproj -m:1 '-p:UnityEditorPath=C:/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor'
```

This is **static C# compilation only**. It does not launch Unity, import assets, load or serialize the scene, run the UI, execute native Unity APIs, or produce a playable build. Those checks require a working Unity Editor session and license. Run the separate `Tests/Core` executable for engine behavior tests.
