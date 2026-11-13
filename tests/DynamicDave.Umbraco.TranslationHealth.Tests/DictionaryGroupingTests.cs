using DynamicDave.Umbraco.TranslationHealth.Dictionary;

namespace DynamicDave.Umbraco.TranslationHealth.Tests;

public class DictionaryGroupingTests
{
    private static DictionaryEntry Entry(string key, Guid? parent = null, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), key, parent, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    private static Dictionary<Guid, DictionaryEntry> ById(params DictionaryEntry[] entries) => entries.ToDictionary(e => e.Id);

    [Fact]
    public void Dotted_key_groups_by_segments()
    {
        var phone = Entry("Footer.Contact.Phone");
        var path = DictionaryGrouping.GroupPath(phone, ById(phone));

        Assert.Equal(["Footer", "Contact"], path);
        Assert.Equal("Footer.Contact", DictionaryGrouping.GroupName(path));
    }

    [Fact]
    public void Dotted_key_ignores_its_tree_parent()
    {
        var parent = Entry("Shop");
        var phone = Entry("Footer.Phone", parent.Id);

        Assert.Equal(["Footer"], DictionaryGrouping.GroupPath(phone, ById(parent, phone)));
    }

    [Fact]
    public void Undotted_key_groups_by_tree_ancestors()
    {
        var footer = Entry("Footer");
        var contact = Entry("Contact", footer.Id);
        var phone = Entry("Phone", contact.Id);

        var path = DictionaryGrouping.GroupPath(phone, ById(footer, contact, phone));

        Assert.Equal(["Footer", "Contact"], path);
    }

    [Fact]
    public void Root_key_without_dots_has_no_group()
    {
        var greeting = Entry("Greeting");

        var path = DictionaryGrouping.GroupPath(greeting, ById(greeting));

        Assert.Empty(path);
        Assert.Equal("", DictionaryGrouping.GroupName(path));
    }

    [Fact]
    public void Tree_cycle_does_not_loop()
    {
        var a = Entry("A");
        var b = Entry("B", a.Id);
        var cyclic = a with { ParentId = b.Id };

        Assert.Equal(2, DictionaryGrouping.GroupPath(Entry("C", b.Id), ById(cyclic, b)).Count);
    }

    [Fact]
    public void Parent_for_new_key_is_longest_existing_prefix()
    {
        var footer = Entry("Footer");
        var contact = Entry("Footer.Contact");
        List<DictionaryEntry> entries = [footer, contact, Entry("Other")];

        Assert.Equal(contact.Id, DictionaryGrouping.ParentForNewKey("Footer.Contact.Fax", entries));
        Assert.Equal(footer.Id, DictionaryGrouping.ParentForNewKey("footer.Address.Street", entries));
        Assert.Null(DictionaryGrouping.ParentForNewKey("New.Thing", entries));
        Assert.Null(DictionaryGrouping.ParentForNewKey("Footer", entries));
    }
}
