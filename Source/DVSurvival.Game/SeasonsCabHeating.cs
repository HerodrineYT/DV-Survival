using System;
using System.Reflection;
using UnityEngine;

namespace DVSurvival.Mod
{
    // Seasons 0.3.12+ owns the physical switch and its authoritative save/MP state.
    // Without it, the existing standalone Survival heater remains available.
    internal static class SeasonsCabHeating
    {
        private static PropertyInfo active;
        private static Func<TrainCar,bool> ensure;
        private static Func<TrainCar,float> get;
        private static Func<string,float> getById;
        private static Func<TrainCar,float> getCabinTemperature;
        private static float nextLookup;
        public static bool Active
        {
            get
            {
                if(active==null && Time.realtimeSinceStartup>=nextLookup)
                {
                    nextLookup=Time.realtimeSinceStartup+2;
                    var type=Type.GetType("DVSeasons.Mod.CabHeating, DVSeasons",false);
                    if(type!=null)
                    {
                        var a=type.GetProperty("IsActive");var e=type.GetMethod("Ensure");
                        var g=type.GetMethod("GetLevel");var s=type.GetMethod("GetLevelById");
                        if(a!=null && e!=null && g!=null && s!=null)
                        {
                            ensure=(Func<TrainCar,bool>)Delegate.CreateDelegate(typeof(Func<TrainCar,bool>),e);
                            get=(Func<TrainCar,float>)Delegate.CreateDelegate(typeof(Func<TrainCar,float>),g);
                            getById=(Func<string,float>)Delegate.CreateDelegate(typeof(Func<string,float>),s);
                            active=a;
                            var climate = type.GetMethod("GetCabinTemperature");
                            if(climate != null) getCabinTemperature = (Func<TrainCar,float>)
                                Delegate.CreateDelegate(typeof(Func<TrainCar,float>), climate);
                        }
                    }
                }
                return active!=null && (bool)active.GetValue(null,null);
            }
        }
        public static bool Ensure(TrainCar car) {return ensure!=null && ensure(car);}
        public static float GetLevel(TrainCar car) {return get!=null ? get(car) : 0;}
        public static float GetLevel(string id) {return getById!=null ? getById(id) : 0;}
        public static float GetCabinTemperature(TrainCar car)
        { return getCabinTemperature != null ? getCabinTemperature(car) : float.NaN; }
    }
}
