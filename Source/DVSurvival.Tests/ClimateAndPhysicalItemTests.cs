using System;
using System.Collections.Generic;
using System.IO;
using DVSurvival.Core;
using DVSurvival.Multiplayer;
using Xunit;

namespace DVSurvival.Tests
{
    public sealed class ClimateAndPhysicalItemTests
    {
        [Fact]
        public void ShutdownImmediatelyStopsCabinHeatingDuringWarmup()
        {
            var cabin=new CabinClimate();
            for(int i=0;i<40;i++) cabin.Advance(.5f,-5,true,1,false,false);
            var before=cabin.AirTemperature;
            for(int i=0;i<60;i++)
            {
                var after=cabin.Advance(.5f,-5,false,1,false,false);
                Assert.True(after <= before); before=after;
            }
        }
        [Fact]
        public void OpenDoorCoolsRunningCabinTowardsOutdoors()
        {
            var cabin=new CabinClimate();
            for(int i=0;i<900;i++) cabin.Advance(.5f,-10,true,1,false,false);
            var before=cabin.AirTemperature;
            for(int i=0;i<120;i++) cabin.Advance(.5f,-10,true,1,true,false);
            Assert.True(cabin.AirTemperature < before-25);
            Assert.InRange(cabin.AirTemperature,-10,-9.5f);
        }
        [Fact]
        public void HotFireboxStillHeatsWithoutDieselEngine()
        {
            var cabin=new CabinClimate();
            for(int i=0;i<120;i++) cabin.Advance(.5f,0,false,0,false,false,45);
            Assert.True(cabin.AirTemperature>35);
        }
        [Fact]
        public void BodyResponseUsesRealSecondsNotDayLength()
        {
            var a=new SurvivalState(); var b=a.Clone();
            var tuning=new SurvivalTuning { ThermalTimeConstantSeconds=36 };
            for(int i=0;i<10;i++)
            {
                SurvivalSimulator.Advance(a,new SurvivalEnvironment { GameHours=1f/3600, AmbientTemperatureCelsius=-10 },tuning,1);
                SurvivalSimulator.Advance(b,new SurvivalEnvironment { GameHours=1f/60, AmbientTemperatureCelsius=-10 },tuning,1);
            }
            Assert.Equal(a.BodyTemperatureCelsius,b.BodyTemperatureCelsius);
            Assert.True(a.BodyTemperatureCelsius<36.5f);
        }
        [Fact]
        public void FiveTimesRateAndHeatedOfficeAreNoticeablyFaster()
        {
            var slow=new SurvivalState { BodyTemperatureCelsius=36 };
            var fast=slow.Clone();
            var env=new SurvivalEnvironment { GameHours=.001f, AmbientTemperatureCelsius=22, ThermalRecoveryMultiplier=2.5f };
            for(int i=0;i<15;i++)
            {
                SurvivalSimulator.Advance(slow,env,new SurvivalTuning { ThermalTimeConstantSeconds=180 },1);
                SurvivalSimulator.Advance(fast,env,new SurvivalTuning { ThermalTimeConstantSeconds=36 },1);
            }
            Assert.True(fast.BodyTemperatureCelsius>36.6f);
            Assert.True(fast.BodyTemperatureCelsius-slow.BodyTemperatureCelsius>.4f);
        }
        [Fact]
        public void PhysicalUseDoesNotSpendLegacyStockOrRepeatHealing()
        {
            var consumed=new HashSet<string>(); var id=Guid.NewGuid().ToString("D");
            var state=new SurvivalState { Health=20, FirstAid=13 };
            var tuning=new SurvivalTuning();
            Assert.Equal(SurvivalResultCode.Success,PhysicalItemLedger.Consume(consumed,id,state,ProvisionKind.FirstAid,tuning));
            Assert.Equal(20,state.Health); Assert.Equal(20,state.FirstAidSecondsRemaining); Assert.Equal(13,state.FirstAid);
            FirstAidRecovery.Advance(state, 10f);
            Assert.Equal(SurvivalResultCode.Success,PhysicalItemLedger.Consume(consumed,id,state,ProvisionKind.FirstAid,tuning));
            Assert.Equal(40,state.Health); Assert.Equal(10,state.FirstAidSecondsRemaining); Assert.Single(consumed);
            FirstAidRecovery.Advance(state, 10f);
            Assert.Equal(60,state.Health);
        }
        [Fact]
        public void UnneededPhysicalItemRemainsUsableLater()
        {
            var consumed=new HashSet<string>(); var id=Guid.NewGuid().ToString("D"); var state=new SurvivalState();
            Assert.Equal(SurvivalResultCode.NotNeeded,PhysicalItemLedger.Consume(consumed,id,state,ProvisionKind.Meal,new SurvivalTuning()));
            Assert.Empty(consumed); state.Hunger=20;
            Assert.Equal(SurvivalResultCode.Success,PhysicalItemLedger.Consume(consumed,id,state,ProvisionKind.Meal,new SurvivalTuning()));
        }
        [Fact]
        public void NativeItemIdentitySurvivesNetworkRoundTrip()
        {
            var value=new SurvivalActionPacket { Request=new SurvivalActionRequest {
                RequestId=42,Action=SurvivalActionKind.ConsumePhysical,Provision=ProvisionKind.Water,
                ItemIdentity=Guid.NewGuid().ToString("D") } };
            using(var stream=new MemoryStream())
            {
                value.Serialize(new BinaryWriter(stream)); stream.Position=0;
                var copy=new SurvivalActionPacket(); copy.Deserialize(new BinaryReader(stream));
                Assert.Equal(value.Request.ItemIdentity,copy.Request.ItemIdentity);
                Assert.Equal(SurvivalActionKind.ConsumePhysical,copy.Request.Action);
            }
        }
    }
}
