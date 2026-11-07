using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using OmsiMapEditor.Core.Localization;
using OmsiMapEditor.OMSI;
using UnityEngine;

namespace OmsiMapEditor.UI
{
    /// <summary>
    /// Opening a map reads its files on a worker thread, so the window keeps answering while a large map comes from a
    /// hard disk; the views take it over once it is read. Reading the open map again, as after an undo or a tile change,
    /// stays immediate, as its callers go on with the new summary and its files were just read.
    /// </summary>
    public sealed partial class EditorShellBootstrap
    {
        /// <summary>A map read from its files, before the views show it.</summary>
        private sealed class MapReading
        {
            public OmsiMapSession Session;
            public OmsiMapSummary Summary;
            public List<OmsiChronoVariant> ChronoVariants;

            /// <summary>The paths as given, under which the map's imagery is stored.</summary>
            public string ImageryKey;
        }

        private Task<MapReading> mapOpening;
        private int mapOpeningGeneration;
        private WelcomeScreen welcomeScreen;

        /// <summary>True while a map is read to be opened.</summary>
        private bool OpeningMap => mapOpening != null;

        /// <summary>Opens the session and reads the map; touches no Unity object, so it runs on any thread.</summary>
        private static MapReading ReadMap(string globalConfigPath, string projectFilePath)
        {
            var session = string.IsNullOrEmpty(projectFilePath)
                ? OmsiMapSession.OpenReadOnly(globalConfigPath)
                : OmsiMapSession.OpenProject(globalConfigPath, projectFilePath);
            return new MapReading
            {
                Session = session,
                Summary = session.Read(),
                ChronoVariants = ReadChronoVariants(session.Source),
                ImageryKey = globalConfigPath + "|" + projectFilePath
            };
        }

        /// <summary>
        /// Reads a map on a worker thread and shows it when it is read, then calls <paramref name="opened"/>. A map opened
        /// meanwhile replaces this one; when reading fails, the status shows <paramref name="failure"/> and the reason.
        /// </summary>
        /// <param name="projectFilePath">The project whose workspace is used; null opens the map read-only.</param>
        private void OpenMapInBackground(string globalConfigPath, string projectName, string projectFilePath, string failure, Action opened)
        {
            var generation = ++mapOpeningGeneration;
            var task = Task.Run(() => ReadMap(globalConfigPath, projectFilePath));
            mapOpening = task;
            welcomeScreen?.SetEnabled(false);
            var name = string.IsNullOrWhiteSpace(projectName)
                ? Path.GetFileName(Path.GetDirectoryName(Path.GetFullPath(globalConfigPath)))
                : projectName;
            SetStatusT("map.status.opening", ("name", name));
            StartCoroutine(FinishOpening(task, generation, projectName, failure, opened));
        }

        private IEnumerator FinishOpening(Task<MapReading> task, int generation, string projectName, string failure, Action opened)
        {
            while (!task.IsCompleted)
                yield return null;
            if (generation != mapOpeningGeneration)
                yield break;
            mapOpening = null;
            welcomeScreen?.SetEnabled(true);
            if (task.IsFaulted)
            {
                var exception = task.Exception?.GetBaseException();
                Debug.LogException(exception);
                SetStatus(failure + exception?.Message);
                yield break;
            }
            try
            {
                ShowMap(task.Result, projectName, false);
                opened?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus(failure + exception.Message);
            }
        }
    }
}
