using Unity.Multiplayer.Hosting.Cli.Provisioning.Aws.Ec2;

namespace Unity.Multiplayer.Hosting.Cli.Tests;

[TestFixture]
public class HttpEc2ApiClientTests
{
    private static HttpClient MakeClient(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler)
    {
        handler = new FakeHttpMessageHandler(respond);
        return new HttpClient(handler) { BaseAddress = new Uri("https://ec2.us-east-1.amazonaws.com/") };
    }

    private static HttpEc2ApiClient MakeSut(Func<HttpRequestMessage, string> respond, out FakeHttpMessageHandler handler) =>
        new(MakeClient(respond, out handler), "AKIAEXAMPLE", "secret", "us-east-1");

    [Test]
    public async Task CreateSecurityGroupAsync_sends_the_name_and_parses_the_group_id_back()
    {
        const string response = """
            <CreateSecurityGroupResponse xmlns="http://ec2.amazonaws.com/doc/2016-11-15/">
              <requestId>r-1</requestId>
              <groupId>sg-abc123</groupId>
            </CreateSecurityGroupResponse>
            """;
        var client = MakeSut(_ => response, out var handler);
        string groupId = await client.CreateSecurityGroupAsync("mp-host-srv", null, CancellationToken.None);

        Assert.That(groupId, Is.EqualTo("sg-abc123"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("Action=CreateSecurityGroup"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("GroupName=mp-host-srv"));
    }

    [Test]
    public async Task RunInstanceAsync_sends_the_image_type_and_key_then_parses_the_launched_instance()
    {
        const string response = """
            <RunInstancesResponse xmlns="http://ec2.amazonaws.com/doc/2016-11-15/">
              <requestId>r-1</requestId>
              <instancesSet>
                <item>
                  <instanceId>i-newinstance</instanceId>
                  <instanceState><code>0</code><name>pending</name></instanceState>
                </item>
              </instancesSet>
            </RunInstancesResponse>
            """;
        var client = MakeSut(_ => response, out var handler);
        var instance = await client.RunInstanceAsync(
            new Ec2RunInstanceRequest("srv", "ami-1", "t3.micro", "my-key", "sg-1", null, "dXNlcmRhdGE="), CancellationToken.None);

        Assert.That(instance.InstanceId, Is.EqualTo("i-newinstance"));
        Assert.That(instance.State, Is.EqualTo("pending"));
        Assert.That(instance.PublicIp, Is.Null);
        Assert.That(handler.RequestBodies.Single(), Does.Contain("ImageId=ami-1"));
        Assert.That(handler.RequestBodies.Single(), Does.Contain("KeyName=my-key"));
    }

    [Test]
    public async Task DescribeInstanceAsync_parses_the_instance_nested_under_reservationSet()
    {
        const string response = """
            <DescribeInstancesResponse xmlns="http://ec2.amazonaws.com/doc/2016-11-15/">
              <requestId>r-1</requestId>
              <reservationSet>
                <item>
                  <instancesSet>
                    <item>
                      <instanceId>i-1</instanceId>
                      <instanceState><code>16</code><name>running</name></instanceState>
                      <ipAddress>198.51.100.10</ipAddress>
                    </item>
                  </instancesSet>
                </item>
              </reservationSet>
            </DescribeInstancesResponse>
            """;
        var client = MakeSut(_ => response, out _);
        var instance = await client.DescribeInstanceAsync("i-1", CancellationToken.None);

        Assert.That(instance.State, Is.EqualTo("running"));
        Assert.That(instance.PublicIp, Is.EqualTo("198.51.100.10"));
    }

    [Test]
    public void An_error_response_surfaces_as_an_exception_with_the_AWS_message()
    {
        const string response = """
            <Response>
              <Errors><Error><Code>AuthFailure</Code><Message>AWS was not able to validate the provided access credentials</Message></Error></Errors>
              <RequestID>r-1</RequestID>
            </Response>
            """;
        var handler = new FakeHttpMessageHandler(_ => response, System.Net.HttpStatusCode.Unauthorized);
        var client = new HttpEc2ApiClient(new HttpClient(handler) { BaseAddress = new Uri("https://ec2.us-east-1.amazonaws.com/") }, "AKIA", "secret", "us-east-1");

        var ex = Assert.ThrowsAsync<InvalidOperationException>(() => client.DescribeInstanceAsync("i-1", CancellationToken.None));
        Assert.That(ex!.Message, Does.Contain("AWS was not able to validate"));
    }
}
