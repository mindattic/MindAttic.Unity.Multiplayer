using System.Collections.Generic;
using NUnit.Framework;

namespace Unity.Multiplayer.Core.Tests
{
    /// <summary>UM-T60: what may be a network key.</summary>
    [TestFixture]
    public class NetKeysTests
    {
        [TestCase("b1")]
        [TestCase("block:12")]
        [TestCase("Level 3 / door A")]
        [TestCase("李")]
        public void GoodKeys_PassValidation(string key)
        {
            Assert.That(NetKeys.Validate(key), Is.Null);
            Assert.That(NetKeys.IsValid(key), Is.True);
        }

        [TestCase(null, "empty")]
        [TestCase("", "empty")]
        [TestCase(" b1", "whitespace")]
        [TestCase("b1 ", "whitespace")]
        [TestCase("b\n1", "control")]
        [TestCase("b\u00001", "control")]
        public void BadKeys_AreRefusedWithAReason(string key, string reason)
        {
            Assert.That(NetKeys.Validate(key), Does.Contain(reason));
            Assert.That(NetKeys.IsValid(key), Is.False);
        }

        [Test]
        public void KeysUpToTheLimit_AreAccepted_AndLongerOnesAreNot()
        {
            Assert.That(NetKeys.IsValid(new string('k', NetKeys.MaxLength)), Is.True);
            Assert.That(NetKeys.Validate(new string('k', NetKeys.MaxLength + 1)), Does.Contain("at most " + NetKeys.MaxLength));
        }

        [Test]
        public void TheDuplicateMessage_NamesTheKeyAndBothObjects()
        {
            string message = NetKeys.DuplicateMessage("b1", "Block A (id 4)", "Block B (id 9)");

            Assert.That(message, Does.Contain("\"b1\""));
            Assert.That(message, Does.Contain("Block A (id 4)"));
            Assert.That(message, Does.Contain("Block B (id 9)"));
        }
    }

    /// <summary>UM-T60: one live object per key, the core of NetRegistry.</summary>
    [TestFixture]
    public class NetKeyTableTests
    {
        private sealed class Item
        {
            public Item(string name) { Name = name; }

            public string Name { get; }

            public override string ToString() => Name;
        }

        [Test]
        public void AnAddedItem_IsFoundByItsKey()
        {
            var table = new NetKeyTable<Item>();
            var a = new Item("a");

            Assert.That(table.Add("b1", a, out Item holder), Is.EqualTo(NetKeyAddResult.Added));
            Assert.That(holder, Is.SameAs(a));
            Assert.That(table.TryGet("b1", out Item found), Is.True);
            Assert.That(found, Is.SameAs(a));
            Assert.That(table.Get("b1"), Is.SameAs(a));
            Assert.That(table.Contains("b1"), Is.True);
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.Keys, Is.EqualTo(new[] { "b1" }));
            Assert.That(table.Items, Is.EqualTo(new[] { a }));
        }

        [Test]
        public void KeysAreCaseSensitive()
        {
            var table = new NetKeyTable<Item>();
            table.Add("b1", new Item("a"), out _);

            Assert.That(table.Contains("B1"), Is.False);
            Assert.That(table.Add("B1", new Item("b"), out _), Is.EqualTo(NetKeyAddResult.Added));
        }

        [Test]
        public void ASecondItemWithAHeldKey_IsRefused_AndTheFirstKeepsIt()
        {
            var table = new NetKeyTable<Item>();
            var first = new Item("first");
            var second = new Item("second");
            table.Add("b1", first, out _);

            Assert.That(table.Add("b1", second, out Item holder), Is.EqualTo(NetKeyAddResult.Duplicate));
            Assert.That(holder, Is.SameAs(first));
            Assert.That(table.Get("b1"), Is.SameAs(first));
            Assert.That(table.Count, Is.EqualTo(1));
        }

        [Test]
        public void AddingTheSameItemTwice_ChangesNothing()
        {
            var table = new NetKeyTable<Item>();
            var a = new Item("a");
            table.Add("b1", a, out _);

            Assert.That(table.Add("b1", a, out Item holder), Is.EqualTo(NetKeyAddResult.AlreadyHeld));
            Assert.That(holder, Is.SameAs(a));
            Assert.That(table.Count, Is.EqualTo(1));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" b1")]
        public void AnInvalidKey_IsRefused(string key)
        {
            var table = new NetKeyTable<Item>();

            Assert.That(table.Add(key, new Item("a"), out Item holder), Is.EqualTo(NetKeyAddResult.InvalidKey));
            Assert.That(holder, Is.Null);
            Assert.That(table.Count, Is.EqualTo(0));
        }

        [Test]
        public void ANullItem_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new NetKeyTable<Item>().Add("b1", null, out _));
        }

        [Test]
        public void Remove_FreesTheKey_SoANewItemCanTakeIt()
        {
            var table = new NetKeyTable<Item>();
            var old = new Item("old");
            var fresh = new Item("new");
            table.Add("b1", old, out _);

            Assert.That(table.Remove("b1", old), Is.True);
            Assert.That(table.Contains("b1"), Is.False);
            Assert.That(table.Add("b1", fresh, out _), Is.EqualTo(NetKeyAddResult.Added));
            Assert.That(table.Get("b1"), Is.SameAs(fresh));
        }

        [Test]
        public void RemovingAnItemThatDoesNotHoldTheKey_LeavesTheHolder()
        {
            var table = new NetKeyTable<Item>();
            var holder = new Item("holder");
            var refused = new Item("refused");
            table.Add("b1", holder, out _);
            table.Add("b1", refused, out _);

            Assert.That(table.Remove("b1", refused), Is.False);
            Assert.That(table.Remove("nope", holder), Is.False);
            Assert.That(table.Remove(null, holder), Is.False);
            Assert.That(table.Get("b1"), Is.SameAs(holder));
        }

        [Test]
        public void MissingKeys_AreNotFound_AndGetSaysWhichKey()
        {
            var table = new NetKeyTable<Item>();

            Assert.That(table.TryGet("b1", out Item item), Is.False);
            Assert.That(item, Is.Null);
            Assert.That(table.TryGet(null, out _), Is.False);
            Assert.That(table.Contains(null), Is.False);
            var ex = Assert.Throws<KeyNotFoundException>(() => table.Get("b1"));
            Assert.That(ex.Message, Does.Contain("\"b1\""));
        }

        [Test]
        public void Clear_ForgetsEveryKey()
        {
            var table = new NetKeyTable<Item>();
            table.Add("b1", new Item("a"), out _);
            table.Add("s1", new Item("b"), out _);

            table.Clear();

            Assert.That(table.Count, Is.EqualTo(0));
            Assert.That(table.Contains("b1"), Is.False);
        }
    }
}
