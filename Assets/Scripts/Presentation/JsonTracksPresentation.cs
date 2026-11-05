using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FlipRace.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace FlipRace.Presentation
{
    public sealed partial class FlipRaceGame
    {
        bool catalogLoading;
        string catalogLoadMessage = "Загружаем трассы…";
        string catalogLoadPhase = "loading";
        string trackImportMessage = "";
        int trackCatalogPage;
        const int TracksPerPage = 8;

        public bool TrackImportVisible { get { return Game != null && showTracks && Trial && (flightPage == 4 || flightPage == 10) && !Portrait; } }
        public Rect TrackImportRect { get { MeasureFlightMenu(); return flightPage == 10 ? new Rect(flightPanel.x + 16, flightPanel.y + 184, flightPanel.width - 32, 44) : FullTrackRect(8); } }
        public string TrackImportStatus { get { return trackImportMessage ?? ""; } }
        public string TrackCatalogState { get { return catalogLoadPhase ?? "loading"; } }
        public string PendingTrackId { get { return pendingFlightTrack ?? (Game == null ? "" : Game.Track.Id); } }
        public int TrackCatalogPage { get { return trackCatalogPage + 1; } }
        public int TrackCatalogPages { get { return TrackPageCount; } }
        int TrackPageCount { get { return Math.Max(1, (RaceTracks.All.Length + TracksPerPage - 1) / TracksPerPage); } }
        Rect TrackPreviousPageRect { get { return new Rect(flightPanel.x + 16, flightPanel.yMax - 44, 44, 44); } }
        Rect TrackNextPageRect { get { return new Rect(flightPanel.x + 68, flightPanel.yMax - 44, 44, 44); } }

        IEnumerator LoadTrackCatalog()
        {
            catalogLoading = true;
            SetCatalogLoadState("loading", "Загружаем список трасс…");
            string manifestJson = null, failure = null;
            yield return ReadTrackFile("index.json", text => manifestJson = text, error => failure = error);
            if (failure != null) { CatalogLoadFailed(failure); yield break; }
            if (!TrackJson.TryReadManifest(manifestJson, out var manifest, out var parseError))
            { CatalogLoadFailed(parseError); yield break; }
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.Files.Length; i++)
            {
                string name = manifest.Files[i];
                SetCatalogLoadState("loading", "Загружаем трассы " + (i + 1) + "/" + manifest.Files.Length + "…");
                string json = null;
                yield return ReadTrackFile(name, text => json = text, error => failure = error);
                if (failure != null) { CatalogLoadFailed(failure); yield break; }
                files.Add(name, json);
            }
            if (!TrackCatalog.TryInstallBuiltIns(manifestJson, files, out var installError))
            { CatalogLoadFailed(installError); yield break; }
            InitializeLoadedGame();
            catalogLoading = false;
            SetCatalogLoadState("ready", "Трассы загружены");
        }
        static IEnumerator ReadTrackFile(string name, Action<string> loaded, Action<string> failed)
        {
            string root = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/Tracks/";
#if !UNITY_WEBGL || UNITY_EDITOR
            root = new Uri(Path.GetFullPath(root).TrimEnd('/', '\\') + Path.DirectorySeparatorChar).AbsoluteUri;
#endif
            using (var request = UnityWebRequest.Get(root + name))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                    failed("Не удалось загрузить " + name + ". " + request.error);
                else loaded(request.downloadHandler.text);
            }
        }
        void InitializeLoadedGame()
        {
#if FLIP_V4_TRIAL
            Game = RaceSimulation.CreateTrial(InitialFlightTrack(), flightSelection.Settings);
            legacyControls = !activeCustomFlight && activeFlightPreset >= 4 && activeFlightPreset <= 9;
#else
            Game = RaceSimulation.CreateGame();
#endif
            selectedTrackId = Game.Track.Id;
            Game.Players[0].CharacterIndex = 0;
            Game.Players[1].CharacterIndex = 1;
#if FLIP_SCREEN_STAGE
            Game = RaceSimulation.CreateTrial(TracksMixed.Get(), flightSelection.Settings);
            selectedTrackId = Game.Track.Id;
            Game.Players[0].CharacterIndex = Game.Players[1].CharacterIndex = 0;
            sharedField = true;
#endif
            lastFrame = Time.realtimeSinceStartupAsDouble;
            suppressFrame = true;
            Measure();
            BlockGameplayInput();
        }
        void SetCatalogLoadState(string phase, string message)
        {
            catalogLoadMessage = message;
            catalogLoadPhase = phase;
            FlipRace.Web.TrackFileBridge.CatalogState(phase, message);
        }
        void CatalogLoadFailed(string error)
        {
            catalogLoading = false;
            SetCatalogLoadState("error", error);
        }
        public void RetryTrackCatalog()
        {
            if (Game == null && !catalogLoading) StartCoroutine(LoadTrackCatalog());
        }
        void DrawCatalogLoading()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            var box = new Rect(Mathf.Max(12, (Screen.width - 520) / 2), Mathf.Max(12, Screen.height / 2 - 70), Mathf.Min(520, Screen.width - 24), 140);
            GUI.Box(box, "Загрузка трасс");
            GUI.Label(new Rect(box.x + 16, box.y + 30, box.width - 32, 64), catalogLoadMessage);
            if (!catalogLoading && GUI.Button(new Rect(box.x + 16, box.yMax - 44, box.width - 32, 36), "Повторить загрузку")) RetryTrackCatalog();
#endif
        }
        public void ImportTrackFile(string json)
        {
            if (!TrackImportVisible || Active)
            { SetTrackImportResult(false, "Импорт доступен в меню трасс до старта."); return; }
            try
            {
                if (string.IsNullOrEmpty(json))
                { SetTrackImportResult(false, "Файл JSON пуст."); return; }
                if (!TrackCatalog.TryImportLocal(json, out var entry, out var error))
                { SetTrackImportResult(false, error); return; }
                pendingFlightTrack = entry.Package.Track.Id;
                for (int i = 0; i < RaceTracks.All.Length; i++)
                    if (RaceTracks.All[i].Id == pendingFlightTrack) { trackCatalogPage = i / TracksPerPage; break; }
                flightPage = 4;
                SetTrackImportResult(true, "Добавлено: " + entry.Package.Track.Name + ". Только в этой сессии.");
                if(entry.Package.References.Length>0)StartCoroutine(VerifyImportedReferences(entry));
            }
            catch (Exception)
            { SetTrackImportResult(false, "Не удалось прочитать файл JSON."); }
            BlockGameplayInput();
        }
        IEnumerator VerifyImportedReferences(TrackCatalogEntry entry)
        {
            // The map is already usable for people. Proof runs in small slices
            // while the player can navigate the menu; no synchronous full replay.
            foreach(var reference in entry.Package.References)
            {
                var flight=reference.ProfileId=="Chosen"?FlightPresets.Chosen:FlightPresets.FastFlip;
                var proof=entry.GetOrCreateProof(flight);
                if(proof==null)continue;
                while(!proof.Complete)
                {
                    double until=Time.realtimeSinceStartupAsDouble+.004;
                    do { proof.Step(1); } while(!proof.Complete&&Time.realtimeSinceStartupAsDouble<until);
                    if(pendingFlightTrack==entry.Package.Track.Id)
                        SetTrackImportResult(true,"Добавлено: "+entry.Package.Track.Name+". "+reference.ProfileId+": "+proof.Progress);
                    yield return null;
                }
            }
            if(pendingFlightTrack==entry.Package.Track.Id)
                SetTrackImportResult(true,"Добавлено: "+entry.Package.Track.Name+". "+TrackCatalog.BotSupportMessage(entry.Package.Track.Id,flightSelection.Settings));
        }
        void SetTrackImportResult(bool success, string message)
        {
            trackImportMessage = message;
            if (!success && Game != null && showTracks && !Active) flightPage = 10;
            FlipRace.Web.TrackFileBridge.ImportResult(success, message);
        }
        public void ImportTrackError(string message)
        { SetTrackImportResult(false, message); if (TrackImportVisible && !Active) BlockGameplayInput(); }
        void DrawTrackImportError()
        {
            label.wordWrap = true;
            Text(new Rect(flightPanel.x + 16, flightPanel.y + 48, flightPanel.width - 32, 120), trackImportMessage, 12, Hex(0x9a6844));
            label.wordWrap = false;
            FlightButton(TrackImportRect, "Выбрать другой JSON · только в этой сессии", false, true, 11);
        }
    }
}
