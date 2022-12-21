﻿// Nu Game Engine.
// Copyright (C) Bryan Edds, 2013-2020.

namespace Nu
open System
open System.Diagnostics
open System.IO
open Prime
module Program =

    /// Program entry point.
    let [<EntryPoint; STAThread>] main _ =

        // ensure template directory exists
        let programDir = Reflection.Assembly.GetEntryAssembly().Location |> Path.GetDirectoryName 
        let slnDir = programDir + "/../../../.." |> Path.Simplify
        let templateDir = programDir + "/../../../Nu.Template" |> Path.Simplify
        if Directory.Exists templateDir then

            // query user to create new project
            Console.Write "Create a new game with the Nu Game Engine? [y/n]: "
            let result = Console.ReadLine ()
            match result.ToUpper () with
            | "Y" ->

                // execute name entry
                Console.Write "Please enter your project's name (no spaces, tabs or dots, PascalCase is preferred): "
                let name = Console.ReadLine ()
                let name = name.Replace(" ", "").Replace("\t", "").Replace(".", "")
                if Array.notExists (fun char -> name.Contains (string char)) (Path.GetInvalidPathChars ()) then
                    
                    // compute directories
                    let templateIdentifier = templateDir.Replace("/", "\\") // this is what dotnet knows the template as for uninstall...
                    let templateFileName = "Nu.Template.fsproj"
                    let projectsDir = programDir + "/../../../../Projects" |> Path.Simplify
                    let newProjDir = projectsDir + "/" + name |> Path.Simplify
                    let newFileName = name + ".fsproj"
                    let newProj = newProjDir + "/" + newFileName |> Path.Simplify
                    Console.WriteLine ("Creating project '" + name + "' in '" + projectsDir + "'...")

                    // install nu template
                    Directory.SetCurrentDirectory templateDir
                    Process.Start("dotnet", "new -u \"" + templateIdentifier + "\"").WaitForExit()
                    Process.Start("dotnet", "new -i ./").WaitForExit()

                    // instantiate nu template
                    Directory.SetCurrentDirectory projectsDir
                    Directory.CreateDirectory name |> ignore<DirectoryInfo>
                    Directory.SetCurrentDirectory newProjDir
                    Process.Start("dotnet", "new nu-game --force").WaitForExit()

                    // rename project file
                    File.Copy (templateFileName, newFileName, true)
                    File.Delete templateFileName

                    // substitute $safeprojectname$ in project file
                    let newProjStr = File.ReadAllText newProj
                    let newProjStr = newProjStr.Replace("$safeprojectname$", name)
                    File.WriteAllText (newProj, newProjStr)

                    // add project to sln file
                    // NOTE: not currently working due to project in old file format - user is instructed to do this
                    // manually.
                    //Directory.SetCurrentDirectory slnDir
                    //Process.Start("dotnet", "sln add Nu.sln \"" + newProj + "\"").WaitForExit()
                    ignore (slnDir, newProj)
                    
                    // success
                    Constants.Engine.ExitCodeSuccess

                // invalid name; failure
                else
                    Console.WriteLine ("Project name '" + name + "' contains invalid path characters.")
                    Constants.Engine.ExitCodeFailure

            // rejected
            | _ -> Constants.Engine.ExitCodeSuccess

        // nothing to do
        else Constants.Engine.ExitCodeSuccess