using System;
using System.Linq;
using FluentAssertions;
using Neo;
using Neo.Assertions;
using Neo.BlockchainToolkit;
using Neo.BlockchainToolkit.Models;
using Neo.BlockchainToolkit.SmartContract;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.VM;
using Neo.VM.Types;
using NeoTestHarness;
using Xunit;
using Array = Neo.VM.Types.Array;

namespace IdoTests
{
    [CheckpointPath("test/bin/checkpoints/contract-deployed.neoxp-checkpoint")]
    public class IdoContractTests : IClassFixture<CheckpointFixture<IdoContractTests>>
    {
        readonly CheckpointFixture fixture;
        readonly ExpressChain chain;

        public IdoContractTests(CheckpointFixture<IdoContractTests> fixture)
        {
            this.fixture = fixture;
            this.chain = fixture.FindChain("IdoTests.neo-express");
        }

        [Fact]
        public void super_admin_in_storage()
        {
            var owner = UInt160.Parse("0xfa03cb7b40072c69ca41f0ad3606a548f1d59966");

            using var snapshot = fixture.GetSnapshot();
            byte[] superAdminKey = { 0x05, 0x01 };

            var storages = snapshot.GetContractStorages<IdoContract>();
            storages.Count().Should().Be(1);
            storages.TryGetValue(superAdminKey, out var item).Should().BeTrue();
            item!.Should().Be(owner);
        }

        [Fact]
        public void default_withdrawal_fee_is_correct()
        {
            var settings = chain.GetProtocolSettings();
            var alice = chain.GetDefaultAccount("alice").ToScriptHash(settings.AddressVersion);

            using var snapshot = fixture.GetSnapshot();

            using var engine = new TestApplicationEngine(snapshot, settings, alice);

            engine.ExecuteScript<IdoContract>(c => c.getWithdrawFee());

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(1);
            engine.ResultStack.Should().Equal(8000);
            engine.ResultStack.Peek(0).Should().BeTrue();
        }

        [Fact]
        public void can_register_project()
        {
            using var snapshot = fixture.GetSnapshot();

            var projectDetails = new ProjectDetails
            {
                tokenAmount = 1000_00000000,
                tokenOfferingPrice = 1,
                allowedLevel = 5,
                tokenContractHash = snapshot.GetContractScriptHash<TestToken>(),
                idoPairContractHash = snapshot.GetContractScriptHash<IdoPairExampleOneContract>()
            };

            RegisterProject(snapshot, projectDetails);

            var settings = chain.GetProtocolSettings();
            var owner = chain.GetDefaultAccount("owner").ToScriptHash(settings.AddressVersion);
            using var engine = new TestApplicationEngine(snapshot, settings, owner);
            engine.ExecuteScript<IdoContract>(c => c.getRegisteredProject(projectDetails.idoPairContractHash));

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(1);

            var result = (Struct) engine.ResultStack.Peek();
            Assert.Equal(projectDetails.tokenAmount, result[1].GetInteger());
            Assert.Equal(projectDetails.tokenOfferingPrice, result[2].GetInteger());
            Assert.Equal(projectDetails.tokenContractHash, new UInt160(result[3].GetSpan()));
            Assert.Equal(projectDetails.allowedLevel, result[7].GetInteger());
        }

        [Fact]
        public void can_retrieve_all_projects()
        {
            using var snapshot = fixture.GetSnapshot();

            var firstProjectDetails = new ProjectDetails
            {
                tokenAmount = 1000_00000000,
                tokenOfferingPrice = 1,
                allowedLevel = 5,
                tokenContractHash = snapshot.GetContractScriptHash<TestToken>(),
                idoPairContractHash = snapshot.GetContractScriptHash<IdoPairExampleOneContract>()
            };

            RegisterProject(snapshot, firstProjectDetails);

            var secondProjectDetails = new ProjectDetails
            {
                tokenAmount = 100_00000000,
                tokenOfferingPrice = 3,
                allowedLevel = 4,
                tokenContractHash = snapshot.GetContractScriptHash<TestToken>(),
                idoPairContractHash = snapshot.GetContractScriptHash<IdoPairExampleTwoContract>(),
            };
            
            RegisterProject(snapshot, secondProjectDetails, false);

            var settings = chain.GetProtocolSettings();
            var owner = chain.GetDefaultAccount("owner").ToScriptHash(settings.AddressVersion);
            using var engine = new TestApplicationEngine(snapshot, settings, owner);
            engine.ExecuteScript<IdoContract>(c => c.getAllRegisteredProjects());

            engine.State.Should().Be(VMState.HALT);
            engine.ResultStack.Should().HaveCount(1);

            var resultArray = (Array) engine.ResultStack.Peek();
            Assert.Equal(firstProjectDetails.idoPairContractHash, new UInt160(resultArray[0].GetSpan()));
            Assert.Equal(secondProjectDetails.idoPairContractHash, new UInt160(resultArray[1].GetSpan()));
        }

        private void RegisterProject(SnapshotCache snapshot, ProjectDetails projectDetails, bool isFirstProject = true)
        {
            var settings = chain.GetProtocolSettings();
            var owner = chain.GetDefaultAccount("owner").ToScriptHash(settings.AddressVersion);

            // Set IDO Pair Asset Hash
            using var engine1 = new TestApplicationEngine(snapshot, settings, owner);
            using var engine2 = new TestApplicationEngine(snapshot, settings, owner);
            using var engine3 = new TestApplicationEngine(snapshot, settings, owner);
            var gasContractHash = UInt160.Parse("0xd2a4cff31913016155e38e474a2c06d08be276cf");
            var tokenContractHash = snapshot.GetContractScriptHash<TestToken>();
            var idoContractHash = snapshot.GetContractScriptHash<IdoContract>();

            if (isFirstProject)
            {
                engine1.ExecuteScript<IdoPairExampleOneContract>(c => c.setAssetHash(gasContractHash));
                engine2.ExecuteScript<IdoPairExampleOneContract>(c => c.setTokenHash(tokenContractHash));
                engine3.ExecuteScript<IdoPairExampleOneContract>(c => c.setIdoContract(idoContractHash));
            }
            else
            {
                engine1.ExecuteScript<IdoPairExampleTwoContract>(c => c.setAssetHash(gasContractHash));
                engine2.ExecuteScript<IdoPairExampleTwoContract>(c => c.setTokenHash(tokenContractHash));
                engine3.ExecuteScript<IdoPairExampleTwoContract>(c => c.setIdoContract(idoContractHash));
            }

            using var engine4 = new TestApplicationEngine(snapshot, settings, owner, WitnessScope.Global);
            engine4.ExecuteScript<IdoContract>(c => c.registerProject(
                projectDetails.tokenAmount,
                projectDetails.tokenOfferingPrice,
                projectDetails.idoPairContractHash,
                projectDetails.allowedLevel,
                projectDetails.tokenContractHash
            ));
        }

        private struct ProjectDetails
        {
            public long tokenAmount;
            public int tokenOfferingPrice;
            public int allowedLevel;
            public UInt160 tokenContractHash;
            public UInt160 idoPairContractHash;
        }
    }
}