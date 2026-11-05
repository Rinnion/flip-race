using System;
using System.Collections.Generic;

namespace FlipRace.Core
{
    public enum TrackOrigin { BuiltIn, Local }

    public sealed class TrackCatalogEntry
    {
        public TrackPackage Package { get; private set; }
        public TrackOrigin Origin { get; private set; }
        readonly Dictionary<string,PreparedReferenceProof> proofs=new Dictionary<string,PreparedReferenceProof>(StringComparer.Ordinal);
        internal TrackCatalogEntry(TrackPackage package, TrackOrigin origin) { Package=package; Origin=origin; }
        public PreparedReferenceProof GetOrCreateProof(FlightSettings flight)
        {
            if(Origin!=TrackOrigin.Local||!Package.TryGetReference(flight,out var reference))return null;
            if(!proofs.TryGetValue(reference.ProfileId,out var proof))
            { proof=new PreparedReferenceProof(Package,flight);proofs.Add(reference.ProfileId,proof); }
            return proof;
        }
        internal bool HasProof(FlightSettings flight)
        { return Package.TryGetReference(flight,out var r)&&proofs.TryGetValue(r.ProfileId,out var p)&&p.Success&&p.Matches(Package,flight); }
        internal string ProofMessage(FlightSettings flight)
        {
            if(!Package.TryGetReference(flight,out var r))return "Для этого управления нет подготовленного маршрута. Доступны человеческие режимы.";
            if(!proofs.TryGetValue(r.ProfileId,out var p)||!p.Complete)return "Проверяем маршрут и Мастера. Пока доступны человеческие режимы.";
            if(!p.Success)return "Бот не допущен: "+p.Failure+". Карта доступна для людей.";
            return HasProof(flight)?"Маршрут и Мастер проверены.":"Содержимое трассы изменилось после проверки. Бот недоступен.";
        }
    }

    // Runtime catalogue is published only after complete validation. Offline
    // factories remain available solely before an external install is attempted.
    public static class TrackCatalog
    {
        static TrackCatalogEntry[] entries=Array.Empty<TrackCatalogEntry>();
        static Dictionary<string,TrackCatalogEntry> lookup=new Dictionary<string,TrackCatalogEntry>(StringComparer.Ordinal);
        public static bool IsInstalled { get; private set; }
        public static bool HasAttemptedInstall { get; private set; }
        public static string LastLoadError { get; private set; }
        public static TrackCatalogEntry[] Entries { get { return (TrackCatalogEntry[])entries.Clone(); } }

        public static bool TryGet(string id,out TrackCatalogEntry entry)
        { entry=null;return id!=null&&lookup.TryGetValue(id,out entry); }
        public static TrackCatalogEntry GetAt(int index)
        { if(index<0||index>=entries.Length)throw new ArgumentOutOfRangeException(nameof(index));return entries[index]; }

        public static bool TryInstallBuiltIns(string manifestJson,Dictionary<string,string> files,out string error)
        {
            HasAttemptedInstall=true;
            if(!TrackJson.TryReadManifest(manifestJson,out var manifest,out error))return Failed(error,out error);
            var next=new List<TrackCatalogEntry>();var ids=new Dictionary<string,TrackCatalogEntry>(StringComparer.Ordinal);
            foreach(string file in manifest.Files)
            {
                if(files==null||!files.TryGetValue(file,out var text))return Failed("Не загружен файл трассы: "+file,out error);
                if(!TrackJson.TryRead(text,out var package,out var reason))return Failed(file+": "+reason,out error);
                string id=package.Track.Id;
                if(IsReservedLegacyId(id))return Failed(file+": ID занят прежним участком: "+id,out error);
                if(ids.ContainsKey(id))return Failed(file+": повторяющийся ID трассы: "+id,out error);
                var entry=new TrackCatalogEntry(package,TrackOrigin.BuiltIn);ids.Add(id,entry);next.Add(entry);
            }
            // A retry/reload must not silently replace an already imported map.
            foreach(var entry in entries)if(entry.Origin==TrackOrigin.Local)
            {
                string id=entry.Package.Track.Id;
                if(ids.ContainsKey(id))return Failed("ID встроенной трассы занят импортированной: "+id,out error);
                ids.Add(id,entry);next.Add(entry);
            }
            entries=next.ToArray();lookup=ids;IsInstalled=true;LastLoadError=null;error=null;return true;
        }
        static bool Failed(string reason,out string error) { LastLoadError=error=reason;return false; }

        public static bool TryImportLocal(string json,out TrackCatalogEntry entry,out string error)
        {
            entry=null;
            if(!IsInstalled){error="Сначала загрузите встроенные трассы.";return false;}
            if(!TrackJson.TryRead(json,out var package,out error))return false;
            string id=package.Track.Id;
            if(IsReservedLegacyId(id)||IsLegacyFullId(id)||lookup.ContainsKey(id))
            {error="ID трассы уже используется или зарезервирован: "+id;return false;}
            entry=new TrackCatalogEntry(package,TrackOrigin.Local);
            var next=new TrackCatalogEntry[entries.Length+1];Array.Copy(entries,next,entries.Length);next[entries.Length]=entry;
            var ids=new Dictionary<string,TrackCatalogEntry>(lookup,StringComparer.Ordinal);ids.Add(id,entry);
            entries=next;lookup=ids;error=null;return true;
        }

        public static bool SupportsProfile(string id,FlightSettings flight)
        {
            if(HasAttemptedInstall)
            {
                if(TryGet(id,out var e))return e.Package.SupportsProfile(flight);
                // Historical practices/control trials remain code-authored and
                // keep their existing independent menu restrictions.
                return flight!=null&&IsCodePracticeId(id);
            }
            return id!=null&&flight!=null&&(id!=TracksRethink.Id||TrackPackage.MatchesProfile("Chosen",flight));
        }
        public static bool UsesAuthoredWarnings(string id)
        {
            if(HasAttemptedInstall)return TryGet(id,out var e)&&e.Package.WarningMode==TrackWarningMode.Authored;
            return id==TracksRethink.Id;
        }
        public static bool HasPreparedBots(string id)
        {
            if(!HasAttemptedInstall)return IsLegacyFullId(id);
            if(!TryGet(id,out var e))return false;
            return e.Origin==TrackOrigin.BuiltIn?e.Package.References.Length>0:e.HasProof(FlightPresets.Chosen)||e.HasProof(FlightPresets.FastFlip);
        }
        // Admission is bound to the current geometry/profile. The simulation
        // factory also recomputes the hash, so mutation cannot reuse a reference.
        public static bool SupportsBot(string id,FlightSettings flight)
        {
            if(!HasAttemptedInstall)return IsLegacyFullId(id)
                &&(TrackPackage.MatchesProfile("Chosen",flight)||(id!=TracksRethink.Id&&TrackPackage.MatchesProfile("FastFlip",flight)));
            return TryGet(id,out var e)&&(e.Origin==TrackOrigin.BuiltIn||e.HasProof(flight))&&e.Package.TryGetReference(flight,out var reference)
                &&reference.TrackHash==e.Package.ContentHash
                &&TrackFingerprint.Compute(e.Package.Track,e.Package.ProfilePolicy,e.Package.WarningMode)==e.Package.ContentHash;
        }
        public static string BotSupportMessage(string id,FlightSettings flight)
        { return TryGet(id,out var e)&&e.Origin==TrackOrigin.Local?e.ProofMessage(flight):"Для этой трассы нет подготовленного бота. Выбери другую трассу или человеческий режим."; }
        internal static bool TryGetBotReference(Track actual,FlightSettings flight,out TrackBotReference reference)
        {
            reference=null;
            if(actual==null||!TryGet(actual.Id,out var e)||(e.Origin!=TrackOrigin.BuiltIn&&!e.HasProof(flight))
                ||!e.Package.TryGetReference(flight,out var found))return false;
            string hash=TrackFingerprint.Compute(actual,e.Package.ProfilePolicy,e.Package.WarningMode);
            if(hash!=e.Package.ContentHash||hash!=found.TrackHash)return false;
            reference=found;return true;
        }

        static bool IsLegacyFullId(string id)
        { return id==TracksFull.LongId||id==TracksFull.ShortId||id==TracksFull.FlowId||id==TracksRhythm.CascadeId||id==TracksMixed.Id
            ||id==TracksHidden.InterceptsId||id==TracksHidden.ManifestationId||id==TracksRethink.Id; }
        public static bool IsReservedImportId(string id)
        { return string.IsNullOrEmpty(id)||IsReservedLegacyId(id)||IsLegacyFullId(id); }
        static bool IsCodePracticeId(string id)
        { return id==TracksFull.PracticeId||id==TracksRhythm.PracticeId||id==TracksControl.ChallengeId||id==TracksControl.UnlimitedId
            ||id==TracksV4.OpenId||id==TracksV4.ChallengeId||id==TracksTune.ChallengeId; }
        static bool IsReservedLegacyId(string id)
        {
            return id==TracksFull.PracticeId||id==TracksRhythm.PracticeId||id==TracksRhythm.DenseId||id==TracksRhythm.WavesId
                ||id==TracksControl.ChallengeId||id==TracksControl.UnlimitedId||id==TracksV4.OpenId||id==TracksV4.ChallengeId||id==TracksTune.ChallengeId
                ||id==TracksMixedStage.StageId||id==TracksMixedStage.ReorderedId||id=="legacy"||id=="legacy-practice"||id=="risk-practice"
                ||id=="hold"||id=="switch"||id=="timing"||(id!=null&&id.StartsWith("v3-",StringComparison.Ordinal));
        }
    }
}
