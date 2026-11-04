using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace FlipRace.Core
{
    // The same bounded JSON grammar, normalization and validation run in .NET
    // tools and Unity. No Unity serializer defaults or external packages.
    public static class TrackJson
    {
        public const int SchemaVersion=1, PhysicsVersion=1, MaximumBytes=2*1024*1024;
        const int MaximumDepth=32, MaximumInputs=4096;
        const double MaximumLength=1000000, MaximumCoordinate=10000000;

        public static bool TryRead(string json,out TrackPackage package,out string error)
        {
            package=null;error=null;
            try { package=ReadPackage(Object(new Reader(json).Read(),"$"));return true; }
            catch(FormatException failure){error=failure.Message;return false;}
            catch(ArgumentException failure){error="Некорректные данные трассы: "+failure.Message;return false;}
        }
        public static bool TryReadManifest(string json,out TrackManifest manifest,out string error)
        {
            manifest=null;error=null;
            try {
                var root=Object(new Reader(json).Read(),"$");Versions(root);
                var files=Strings(Required(root,"files","$"),"$.files",128);Require(files.Length>0,"$.files","Каталог пуст.");
                var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string file in files){
                    bool asciiLeaf=true;foreach(char c in file)if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9')||c=='.'||c=='_'||c=='-')){asciiLeaf=false;break;}
                    Require(asciiLeaf&&file.Length<=160&&file.EndsWith(".json",StringComparison.Ordinal)
                        &&file.IndexOfAny(new[]{'/','\\',':','?','#','\0'})<0&&file!=".json"&&file!="..json"
                        &&file.IndexOf("..",StringComparison.Ordinal)<0,"$.files","Нужно относительное имя JSON-файла без пути или URL.");
                    Require(unique.Add(file),"$.files","Повтор имени файла: "+file);
                }
                manifest=new TrackManifest(files);return true;
            }catch(FormatException failure){error=failure.Message;return false;}
        }
        public static string Write(TrackPackage package,bool pretty=true)
        {
            if(package==null)throw new ArgumentNullException(nameof(package));
            var references=new List<object>();foreach(var r in package.References){
                if(r==null)throw new ArgumentException("Пустой эталон.",nameof(package));
                references.Add(Fields("profileId",r.ProfileId,"trackHash",r.TrackHash,"inputs",r.Inputs,"rewardIds",r.RewardIds,"bonusGain",r.BonusGain));
            }
            var root=Fields("schemaVersion",SchemaVersion,"physicsVersion",PhysicsVersion,
                "profilePolicy",package.ProfilePolicy==TrackProfilePolicy.Any?"any":"chosen-only",
                "warningMode",package.WarningMode==TrackWarningMode.Selected?"selected":"authored",
                "track",TrackData(package.Track),"botReferences",references);
            // Export uses exactly the same semantic validator as runtime import.
            ReadPackage(root);return Print(root,pretty);
        }
        public static string WriteManifest(TrackManifest manifest,bool pretty=true)
        {
            if(manifest==null)throw new ArgumentNullException(nameof(manifest));
            string text=Print(Fields("schemaVersion",SchemaVersion,"physicsVersion",PhysicsVersion,"files",manifest.Files),pretty);
            if(!TryReadManifest(text,out var ignored,out var error))throw new ArgumentException(error,nameof(manifest));return text;
        }
        static TrackPackage ReadPackage(Dictionary<string,object> root)
        {
            Versions(root);
            string policyText=String(root,"profilePolicy","$",false,"any"),warningText=String(root,"warningMode","$",false,"selected");
            Require(policyText=="any"||policyText=="chosen-only","$.profilePolicy","Неизвестное ограничение управления.");
            Require(warningText=="selected"||warningText=="authored","$.warningMode","Неизвестный режим предупреждения.");
            var policy=policyText=="any"?TrackProfilePolicy.Any:TrackProfilePolicy.ChosenOnly;
            var warning=warningText=="selected"?TrackWarningMode.Selected:TrackWarningMode.Authored;
            var data=Object(Required(root,"track","$"),"$.track");
            string id=Id(data,"id","$.track"),name=String(data,"name","$.track",true);
            Require(name.Length>0&&name.Length<=160,"$.track.name","Название должно содержать от 1 до 160 символов.");
            double length=Number(data,"length","$.track",true);Range(length,0,MaximumLength,"$.track.length",true);
            var obstaclesData=Array(Required(data,"obstacles","$.track"),"$.track.obstacles",1024);
            var obstacles=new List<Obstacle>();var obstacleIds=new Dictionary<string,Obstacle>(StringComparer.Ordinal);
            for(int i=0;i<obstaclesData.Count;i++){
                string p="$.track.obstacles["+i+"]";var d=Object(obstaclesData[i],p);
                var o=new Obstacle{Id=Id(d,"id",p),Kind=String(d,"kind",p,true),X=Number(d,"x",p,true),Width=Number(d,"width",p,true),
                    Height=Number(d,"height",p),GapTop=Number(d,"gapTop",p),GapBottom=Number(d,"gapBottom",p),
                    TopOffsetX=Number(d,"topOffsetX",p),BottomOffsetX=Number(d,"bottomOffsetX",p),Hidden=Boolean(d,"hidden",p),
                    HiddenRevealDistance=NullableNumber(d,"hiddenRevealDistance",p),HiddenWarningDuration=NullableNumber(d,"hiddenWarningDuration",p)};
                Require(o.Kind=="floor"||o.Kind=="ceiling"||o.Kind=="gate",p+".kind","Неизвестный тип препятствия: "+o.Kind);
                Require(!obstacleIds.ContainsKey(o.Id),p+".id","Повтор ID препятствия: "+o.Id);
                Range(o.X,0,length,p+".x");Range(o.Width,0,MaximumLength,p+".width",true);
                Range(o.Height,0,RaceConfig.CorridorHeight,p+".height");Range(o.GapTop,0,RaceConfig.CorridorHeight,p+".gapTop");Range(o.GapBottom,0,RaceConfig.CorridorHeight,p+".gapBottom");
                if(o.Kind=="gate"){
                    Required(d,"gapTop",p);Required(d,"gapBottom",p);
                    Require(o.GapTop>0&&o.GapBottom<RaceConfig.CorridorHeight,p,"Ворота должны иметь обе ненулевые части.");
                    Require(!o.Hidden,p+".hidden","Скрытыми могут быть только обычные камни.");
                }else {Required(d,"height",p);Require(o.Height>0,p+".height","Высота камня должна быть положительной.");}
                if(o.HiddenRevealDistance.HasValue)Range(o.HiddenRevealDistance.Value,0,MaximumCoordinate,p+".hiddenRevealDistance",true);
                if(o.HiddenWarningDuration.HasValue)Range(o.HiddenWarningDuration.Value,1d/240,60,p+".hiddenWarningDuration",true);
                for(int band=0;band<2;band++)if(ObstacleGeometry.TryGetBand(o,band,out var shape))
                    Require(shape.Left>=0&&shape.Right<=length,p,"Физическая часть препятствия выходит за длину трассы.");
                obstacles.Add(o);obstacleIds.Add(o.Id,o);
            }
            var zones=new List<RiskZone>();var zoneIds=new Dictionary<string,RiskZone>(StringComparer.Ordinal);
            var zoneData=OptionalArray(data,"riskZones","$.track",4096);int finishCount=0;
            for(int i=0;i<zoneData.Count;i++){
                string p="$.track.riskZones["+i+"]";var d=Object(zoneData[i],p);
                var z=new RiskZone {Id=Id(d,"id",p),Kind=String(d,"kind",p,true),ObstacleId=String(d,"obstacleId",p,false,null,true),
                    Placement=String(d,"placement",p,false,"before"),FreeReward=Boolean(d,"freeReward",p),X=Number(d,"x",p,true),Y=Number(d,"y",p,true),
                    Radius=Number(d,"radius",p,false,6),ClearX=Number(d,"clearX",p),SafeFlipX=Number(d,"safeFlipX",p),RiskFlipX=Number(d,"riskFlipX",p),
                    LandingWidth=Number(d,"landingWidth",p),FinishWidth=Number(d,"finishWidth",p,false,32),FinishHeight=Number(d,"finishHeight",p,false,8)};
                Require(z.Kind=="positive"||z.Kind=="instant"||z.Kind=="landing"||z.Kind=="negative"||z.Kind=="coin"||z.Kind=="finish",p+".kind","Неизвестный тип зоны: "+z.Kind);
                Require(z.Placement=="before"||z.Placement=="after",p+".placement","Ожидается before или after.");
                Require(!zoneIds.ContainsKey(z.Id),p+".id","Повтор ID зоны: "+z.Id);
                Require(!obstacleIds.ContainsKey(z.Id),p+".id","ID зоны совпадает с ID препятствия: "+z.Id);
                Range(z.X,0,length,p+".x");Range(z.Y,0,RaceConfig.CorridorHeight,p+".y");Range(z.Radius,0,RaceConfig.CorridorHeight,p+".radius",true);
                Range(z.ClearX,0,length,p+".clearX");Range(z.SafeFlipX,0,length,p+".safeFlipX");Range(z.RiskFlipX,0,length,p+".riskFlipX");
                Range(z.LandingWidth,0,MaximumLength,p+".landingWidth");Range(z.FinishWidth,0,MaximumLength,p+".finishWidth");Range(z.FinishHeight,0,RaceConfig.CorridorHeight,p+".finishHeight");
                Obstacle linked=null;
                if(z.ObstacleId!=null)Require(obstacleIds.TryGetValue(z.ObstacleId,out linked),p+".obstacleId","Нет связанного препятствия: "+z.ObstacleId);
                if(z.IsLanding){
                    Require(linked!=null&&(linked.Kind=="floor"||linked.Kind=="ceiling"),p,"Посадочная награда требует связанного камня.");
                    Require(z.LandingWidth>0&&Math.Max(linked.X,z.X-z.LandingWidth/2)<=Math.Min(linked.X+linked.Width,z.X+z.LandingWidth/2),p+".landingWidth","Посадочная метка не пересекает поверхность камня.");
                }
                if(z.IsFinish){finishCount++;Require(z.X<length,p+".x","Финишная награда должна быть перед лентой.");}
                zones.Add(z);zoneIds.Add(z.Id,z);
            }
            Require(finishCount<=1,"$.track.riskZones","В v1 допустима одна финишная награда.");
            var cues=new List<Cue>();var cueData=OptionalArray(data,"cues","$.track",1024);
            for(int i=0;i<cueData.Count;i++){
                string p="$.track.cues["+i+"]";var d=Object(cueData[i],p);double x=Number(d,"x",p,true);Range(x,0,length,p+".x");
                string text=String(d,"text",p,true);Require(text.Length<=512,p+".text","Слишком длинная подсказка.");cues.Add(new Cue(x,text));
            }
            var track=new Track(length,obstacles.ToArray(),cues.ToArray()){Id=id,Name=name,Focus=String(data,"focus","$.track",false,""),
                RiskZones=zones.ToArray(),NegativeEnabled=Boolean(data,"negativeEnabled","$.track"),
                SafeRoute=Schedule(data,"safeRoute","$.track",length,false),RiskRoute=Schedule(data,"riskRoute","$.track",length,false)};
            Require(track.Focus.Length<=2048,"$.track.focus","Слишком длинное описание.");
            if(warning==TrackWarningMode.Authored){
                bool any=false;foreach(var o in track.Obstacles)if(o.Hidden){any=true;Require(o.HiddenWarningDuration.HasValue,"$.warningMode","Режим authored требует длительность каждого скрытого камня.");}
                Require(any,"$.warningMode","Режим authored требует скрытые камни.");
            }
            string hash=TrackFingerprint.Compute(track,policy,warning);var references=new List<TrackBotReference>();var profiles=new HashSet<string>(StringComparer.Ordinal);
            var referenceData=OptionalArray(root,"botReferences","$",2);
            for(int i=0;i<referenceData.Count;i++){
                string p="$.botReferences["+i+"]";var d=Object(referenceData[i],p);string profile=String(d,"profileId",p,true),trackHash=String(d,"trackHash",p,true);
                Require(profile=="Chosen"||profile=="FastFlip",p+".profileId","Неизвестный профиль эталона.");Require(profiles.Add(profile),p+".profileId","Повтор профиля эталона.");
                Require(policy!=TrackProfilePolicy.ChosenOnly||profile=="Chosen",p+".profileId","Профиль запрещён ограничением трассы.");
                Require(string.Equals(hash,trackHash,StringComparison.OrdinalIgnoreCase),p+".trackHash","Эталон устарел: хеш не совпадает с содержимым трассы.");
                var inputs=Schedule(d,"inputs",p,length,true);Require(inputs.Length>0,p+".inputs","Эталон не содержит нажатий.");
                var rewards=Strings(Required(d,"rewardIds",p),p+".rewardIds",4096);var selected=new HashSet<string>(StringComparer.Ordinal);int ordinary=0,finish=0;
                foreach(string reward in rewards){
                    Require(selected.Add(reward),p+".rewardIds","Повтор ID награды: "+reward);
                    Require(zoneIds.TryGetValue(reward,out var zone)&&!zone.IsNegative&&!zone.IsCoin,p+".rewardIds","Нет положительной награды: "+reward);
                    if(zone.IsFinish)finish++;else ordinary++;
                }
                Require(finish==1,p+".rewardIds","Эталон должен включать одну финишную награду.");
                double gain=Number(d,"bonusGain",p,true),expected=ordinary*RaceConfig.BoostTimeGain+RaceConfig.FinishBoostDuration*(RaceConfig.FinishBoostSpeed/RaceConfig.Speed-1);
                Require(gain>0&&Math.Abs(gain-expected)<=1e-9,p+".bonusGain","B не совпадает с полными эффектами выбранных наград.");
                references.Add(new TrackBotReference(profile,hash,inputs,rewards,gain));
            }
            return new TrackPackage(track,policy,warning,references.ToArray());
        }
        static void Versions(Dictionary<string,object> root)
        {
            Require(Number(root,"schemaVersion","$",true)==SchemaVersion,"$.schemaVersion","Неподдерживаемая версия JSON-схемы.");
            Require(Number(root,"physicsVersion","$",true)==PhysicsVersion,"$.physicsVersion","Неподдерживаемая версия физики.");
        }
        static double[] Schedule(Dictionary<string,object> d,string key,string path,double length,bool required)
        {
            if(!d.TryGetValue(key,out var value)){Require(!required,path+"."+key,"Обязательное поле отсутствует.");return System.Array.Empty<double>();}
            var values=Array(value,path+"."+key,MaximumInputs);var result=new double[values.Count];double previous=-1;
            for(int i=0;i<result.Length;i++){string p=path+"."+key+"["+i+"]";double number=Numeric(values[i],p);Require(number>=0&&number<length&&number>previous,p,"Нажатия должны строго возрастать и лежать до финиша.");result[i]=number;previous=number;}
            return result;
        }
        static Dictionary<string,object> TrackData(Track t)
        {
            var obstacles=new List<object>();foreach(var o in t.Obstacles)obstacles.Add(Fields("id",o.Id,"kind",o.Kind,"x",o.X,"width",o.Width,"height",o.Height,
                "gapTop",o.GapTop,"gapBottom",o.GapBottom,"topOffsetX",o.TopOffsetX,"bottomOffsetX",o.BottomOffsetX,"hidden",o.Hidden,
                "hiddenRevealDistance",o.HiddenRevealDistance,"hiddenWarningDuration",o.HiddenWarningDuration));
            var zones=new List<object>();foreach(var z in t.RiskZones)zones.Add(Fields("id",z.Id,"kind",z.Kind,"obstacleId",z.ObstacleId,"placement",z.Placement,
                "freeReward",z.FreeReward,"x",z.X,"y",z.Y,"radius",z.Radius,"clearX",z.ClearX,"safeFlipX",z.SafeFlipX,"riskFlipX",z.RiskFlipX,
                "landingWidth",z.LandingWidth,"finishWidth",z.FinishWidth,"finishHeight",z.FinishHeight));
            var cues=new List<object>();foreach(var c in t.Cues)cues.Add(Fields("x",c.X,"text",c.Text));
            return Fields("id",t.Id,"name",t.Name,"focus",t.Focus,"length",t.Length,"obstacles",obstacles,"riskZones",zones,"cues",cues,
                "safeRoute",t.SafeRoute,"riskRoute",t.RiskRoute,"negativeEnabled",t.NegativeEnabled);
        }
        static Dictionary<string,object> Fields(params object[] values){var d=new Dictionary<string,object>(StringComparer.Ordinal);for(int i=0;i<values.Length;i+=2)d.Add((string)values[i],values[i+1]);return d;}
        static void Require(bool ok,string path,string message){if(!ok)throw new FormatException(path+": "+message);}
        static Dictionary<string,object> Object(object value,string path){Require(value is Dictionary<string,object>,path,"Ожидается JSON-объект.");return (Dictionary<string,object>)value;}
        static object Required(Dictionary<string,object> d,string key,string path){Require(d.TryGetValue(key,out var value)&&value!=null,path+"."+key,"Обязательное поле отсутствует или равно null.");return value;}
        static string String(Dictionary<string,object> d,string key,string path,bool required=false,string fallback=null,bool nullable=false)
        {
            if(!d.TryGetValue(key,out var value)){Require(!required,path+"."+key,"Обязательная строка отсутствует.");return fallback;}
            if(value==null&&nullable)return null;Require(value is string,path+"."+key,"Ожидается строка, не null.");return (string)value;
        }
        static string Id(Dictionary<string,object> d,string key,string path){string id=String(d,key,path,true);Require(!string.IsNullOrWhiteSpace(id)&&id.Length<=128,path+"."+key,"ID должен содержать от 1 до 128 символов.");foreach(char c in id)Require(!char.IsControl(c),path+"."+key,"Управляющий символ в ID.");return id;}
        static double Numeric(object value,string path)
        {
            double number=value is double?(double)value:value is int?(int)value:double.NaN;
            Require(!double.IsNaN(number)&&!double.IsInfinity(number),path,"Ожидается конечное JSON-число, не строка/null.");return number;
        }
        static double Number(Dictionary<string,object> d,string key,string path,bool required=false,double fallback=0)
        {if(!d.TryGetValue(key,out var value)){Require(!required,path+"."+key,"Обязательное число отсутствует.");return fallback;}return Numeric(value,path+"."+key);}
        static double? NullableNumber(Dictionary<string,object> d,string key,string path){return !d.TryGetValue(key,out var value)||value==null?(double?)null:Numeric(value,path+"."+key);}
        static bool Boolean(Dictionary<string,object> d,string key,string path){if(!d.TryGetValue(key,out var value))return false;Require(value is bool,path+"."+key,"Ожидается true или false.");return (bool)value;}
        static List<object> Array(object value,string path,int maximum){Require(value is IList,path,"Ожидается JSON-массив.");var list=new List<object>();foreach(var item in (IList)value){Require(list.Count<maximum,path,"Слишком много элементов.");list.Add(item);}return list;}
        static List<object> OptionalArray(Dictionary<string,object> d,string key,string path,int maximum){return d.TryGetValue(key,out var value)?Array(value,path+"."+key,maximum):new List<object>();}
        static string[] Strings(object value,string path,int maximum){var list=Array(value,path,maximum);var result=new string[list.Count];for(int i=0;i<result.Length;i++){Require(list[i] is string&&!string.IsNullOrWhiteSpace((string)list[i]),path+"["+i+"]","Ожидается непустая строка.");result[i]=(string)list[i];}return result;}
        static void Range(double value,double minimum,double maximum,string path,bool exclusiveMinimum=false){Require(!double.IsNaN(value)&&!double.IsInfinity(value)&&(exclusiveMinimum?value>minimum:value>=minimum)&&value<=maximum,path,"Число выходит за допустимые границы.");}

        static string Print(object value,bool pretty){var text=new StringBuilder();WriteValue(text,value,pretty,0);return text.ToString()+"\n";}
        static void WriteValue(StringBuilder text,object value,bool pretty,int depth)
        {
            if(value==null){text.Append("null");return;}
            if(value is string){Quote(text,(string)value);return;}
            if(value is bool){text.Append((bool)value?"true":"false");return;}
            if(value is double||value is int){double number=Numeric(value,"$");text.Append(number.ToString("R",CultureInfo.InvariantCulture));return;}
            if(value is Dictionary<string,object> d){
                text.Append('{');var keys=new List<string>(d.Keys);keys.Sort(StringComparer.Ordinal);bool first=true;
                foreach(string key in keys){if(!first)text.Append(',');NewLine(text,pretty,depth+1);Quote(text,key);text.Append(pretty?": ":":");WriteValue(text,d[key],pretty,depth+1);first=false;}
                if(!first)NewLine(text,pretty,depth);text.Append('}');return;
            }
            if(value is IEnumerable sequence){text.Append('[');bool first=true;foreach(var item in sequence){if(!first)text.Append(',');NewLine(text,pretty,depth+1);WriteValue(text,item,pretty,depth+1);first=false;}if(!first)NewLine(text,pretty,depth);text.Append(']');return;}
            throw new ArgumentException("Неподдерживаемое значение JSON.");
        }
        static void NewLine(StringBuilder text,bool pretty,int depth){if(pretty){text.Append('\n');text.Append(' ',depth*2);}}
        static void Quote(StringBuilder text,string value)
        {
            text.Append('"');foreach(char c in value){switch(c){case '"':text.Append("\\\"");break;case '\\':text.Append("\\\\");break;case '\b':text.Append("\\b");break;case '\f':text.Append("\\f");break;case '\n':text.Append("\\n");break;case '\r':text.Append("\\r");break;case '\t':text.Append("\\t");break;default:if(c<32)text.Append("\\u"+((int)c).ToString("x4",CultureInfo.InvariantCulture));else text.Append(c);break;}}text.Append('"');
        }

        sealed class Reader
        {
            readonly string text;int at,nodes;
            public Reader(string text){Require(text!=null,"$","Файл пуст.");Require(text.Length<=MaximumBytes&&Encoding.UTF8.GetByteCount(text)<=MaximumBytes,"$","JSON больше 2 МиБ.");this.text=text;if(text.Length>0&&text[0]=='\ufeff')at=1;}
            public object Read(){var value=Value(0);White();Require(at==text.Length,"$","Лишние данные после JSON (позиция "+at+").");return value;}
            void White(){while(at<text.Length&&(text[at]==' '||text[at]=='\t'||text[at]=='\r'||text[at]=='\n'))at++;}
            void Error(string message){throw new FormatException("JSON, позиция "+at+": "+message);}
            bool Take(char c){White();if(at<text.Length&&text[at]==c){at++;return true;}return false;}
            object Value(int depth)
            {
                if(depth>MaximumDepth||++nodes>100000)Error("Слишком сложная структура.");White();if(at>=text.Length)Error("Неожиданный конец файла.");char c=text[at];
                if(c=='{'){
                    at++;var d=new Dictionary<string,object>(StringComparer.Ordinal);if(Take('}'))return d;
                    do{White();if(at>=text.Length||text[at]!='"')Error("Ожидается имя поля.");string key=Text();Require(key.Length<=256,"$","Слишком длинное имя поля.");if(!Take(':'))Error("Ожидается двоеточие.");if(d.ContainsKey(key))Error("Повтор JSON-поля: "+key);if(d.Count>=512)Error("Слишком много полей объекта.");d.Add(key,Value(depth+1));if(Take('}'))return d;}while(Take(','));Error("Ожидается запятая или закрывающая скобка.");
                }
                if(c=='['){at++;var values=new List<object>();if(Take(']'))return values;do{if(values.Count>=8192)Error("Слишком длинный массив.");values.Add(Value(depth+1));if(Take(']'))return values;}while(Take(','));Error("Ожидается запятая или закрывающая скобка.");}
                if(c=='"')return Text();if(c=='t'){Literal("true");return true;}if(c=='f'){Literal("false");return false;}if(c=='n'){Literal("null");return null;}
                if(c=='-'||(c>='0'&&c<='9'))return Number();Error("Неизвестное JSON-значение.");return null;
            }
            void Literal(string value){if(at+value.Length>text.Length||text.Substring(at,value.Length)!=value)Error("Некорректное JSON-слово.");at+=value.Length;}
            double Number()
            {
                int begin=at;if(text[at]=='-')at++;if(at>=text.Length)Error("Число не завершено.");
                if(text[at]=='0'){at++;if(at<text.Length&&Digit(text[at]))Error("Лишний ведущий ноль.");}
                else {if(text[at]<'1'||text[at]>'9')Error("Ожидается цифра.");while(at<text.Length&&Digit(text[at]))at++;}
                if(at<text.Length&&text[at]=='.'){at++;int digits=at;while(at<text.Length&&Digit(text[at]))at++;if(at==digits)Error("Нет цифр после точки.");}
                if(at<text.Length&&(text[at]=='e'||text[at]=='E')){at++;if(at<text.Length&&(text[at]=='+'||text[at]=='-'))at++;int digits=at;while(at<text.Length&&Digit(text[at]))at++;if(at==digits)Error("Нет цифр экспоненты.");}
                if(!double.TryParse(text.Substring(begin,at-begin),NumberStyles.Float,CultureInfo.InvariantCulture,out double result)||double.IsNaN(result)||double.IsInfinity(result))Error("Число не конечное.");return result;
            }
            static bool Digit(char c){return c>='0'&&c<='9';}
            string Text()
            {
                at++;var value=new StringBuilder();
                while(at<text.Length){char c=text[at++];if(c=='"')return value.ToString();if(c<32)Error("Управляющий символ в строке.");
                    if(c=='\\'){
                        if(at>=text.Length)Error("Обрыв escape-последовательности.");char escape=text[at++];
                        switch(escape){case '"':c='"';break;case '\\':c='\\';break;case '/':c='/';break;case 'b':c='\b';break;case 'f':c='\f';break;case 'n':c='\n';break;case 'r':c='\r';break;case 't':c='\t';break;case 'u':c=Unicode();break;default:Error("Неизвестная escape-последовательность.");break;}
                        if(char.IsHighSurrogate(c)){value.Append(c);if(at+1>=text.Length||text[at]!='\\'||text[at+1]!='u')Error("Нет второй части Unicode-пары.");at+=2;c=Unicode();if(!char.IsLowSurrogate(c))Error("Неверная Unicode-пара.");}
                        else if(char.IsLowSurrogate(c))Error("Одиночная вторая часть Unicode-пары.");
                    }else if(char.IsHighSurrogate(c)){value.Append(c);if(at>=text.Length||!char.IsLowSurrogate(text[at]))Error("Неверная Unicode-пара.");c=text[at++];}
                    else if(char.IsLowSurrogate(c))Error("Одиночная вторая часть Unicode-пары.");
                    value.Append(c);if(value.Length>8192)Error("Слишком длинная строка.");
                }
                Error("Строка не закрыта.");return null;
            }
            char Unicode(){if(at+4>text.Length)Error("Обрыв Unicode escape.");int code=0;for(int i=0;i<4;i++){char c=text[at++];int digit=c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;if(digit<0)Error("Неверный Unicode escape.");code=code*16+digit;}return (char)code;}
        }
    }
}
