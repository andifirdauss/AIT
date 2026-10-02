using AiThread.Service.Models;

namespace AiThread.Tests;

public class ChatMessagesTests
{
    [Fact]
    public void User_without_images_keeps_plain_string_content()
    {
        var message = ChatMessages.User("hello", images: null);

        Assert.Equal("user", (string?)message["role"]);
        Assert.Equal("hello", (string?)message["content"]);
    }

    [Fact]
    public void User_with_images_becomes_a_vision_parts_array()
    {
        var message = ChatMessages.User("What's disabled here?", [new ImageAttachment("image/png", "aGVsbG8=")]);

        var parts = message["content"]!.AsArray();
        Assert.Equal(2, parts.Count);
        Assert.Equal("text", (string?)parts[0]!["type"]);
        Assert.Equal("What's disabled here?", (string?)parts[0]!["text"]);
        Assert.Equal("image_url", (string?)parts[1]!["type"]);
        Assert.Equal("data:image/png;base64,aGVsbG8=", (string?)parts[1]!["image_url"]!["url"]);
    }
}
