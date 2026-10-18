using System.Buffers.Binary;
using Android.Emulation.Control;
using SekaiPad;
using RawInput.Touchpad;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("FAIL: " + message);
}

static TouchEvent Frame(params (int Id, int X, int Y, int Pressure)[] points)
{
    var f = new TouchEvent { Display = 0 };
    foreach (var c in points)
        f.Touches.Add(new Touch { Identifier=c.Id, X=c.X, Y=c.Y, Pressure=c.Pressure });
    return f;
}

var codec = new ScrcpyTouchPackets();
var down = Frame((101, 450, 540, 80), (102, 1450, 540, 80));
var packets = codec.Translate(down, 1920, 1080);
Assert(packets.Count == 2, "two DOWN packets");
Assert(packets.All(p => p.Length == 32 && p[0] == 2 && p[1] == 0), "scrcpy DOWN framing");
Assert(BinaryPrimitives.ReadUInt64BigEndian(packets[0].AsSpan(2,8)) == 101, "stable first ID");
Assert(BinaryPrimitives.ReadInt32BigEndian(packets[0].AsSpan(10,4)) == 450, "first X");
Assert(BinaryPrimitives.ReadUInt16BigEndian(packets[1].AsSpan(20,2)) == 1080, "second screen height");
Assert(BinaryPrimitives.ReadUInt16BigEndian(packets[0].AsSpan(22,2)) == ushort.MaxValue, "DOWN pressure");
Assert(codec.ActiveCount == 2, "active after two DOWN");
Assert(codec.Translate(down, 1920, 1080).Count == 0, "unchanged frame suppressed");
var moved = Frame((101, 475, 520, 80), (102, 1475, 520, 80));
var movePackets = codec.Translate(moved, 1920, 1080);
Assert(movePackets.Count == 2 && movePackets.All(p=>p[1] == 2), "two MOVE packets");
var release = Frame((101, 475, 520, 0), (102, 1475, 520, 0));
var releasePackets = codec.Translate(release, 1920, 1080);
Assert(releasePackets.Count == 2 && releasePackets.All(p=>p[1]==1), "two UP packets");
Assert(BinaryPrimitives.ReadUInt16BigEndian(releasePackets[0].AsSpan(22,2)) == 0, "UP pressure 0");
Assert(codec.ActiveCount == 0, "all contacts released");
Assert(codec.ReleaseAll(1920, 1080).Count == 0, "releases are idempotent");
var map = new TouchMapping { ScreenWidth=1920, ScreenHeight=1080 };
var rawDown = new[] { new TouchpadContact(31, 500, 3000), new TouchpadContact(32, 3500, 3000) };
var mapped = map.Update(rawDown, 0, 4095, 0, 4095);
Assert(mapped is not null && mapped.Touches.Count==2, "PTP two-finger mapping");
var mapperCodec = new ScrcpyTouchPackets();
Assert(mapperCodec.Translate(mapped!, 1920, 1080).Count==2, "PTP -> 2 scrcpy down packets");
var upFlick = map.Update(new[] { new TouchpadContact(31, 500, 2500),
                                new TouchpadContact(32, 3500, 3000) },
                         0, 4095, 0, 4095);
Assert(upFlick is not null && upFlick.Touches.First(t=>t.Identifier==31).Y <
       mapped!.Touches.First(t=>t.Identifier==31).Y, "physical upward flick moves screen upward");
Assert(mapperCodec.Translate(upFlick!, 1920, 1080).Count==1,
       "only moving contact produces MOVE");
var rawUp = map.Update(Array.Empty<TouchpadContact>(),0,4095,0,4095);
Assert(rawUp is not null && rawUp.Touches.Count==2 &&
       rawUp.Touches.All(t=>t.Pressure==0), "PTP release frame");
Assert(mapperCodec.Translate(rawUp!,1920,1080).Count==2 &&
       mapperCodec.ActiveCount==0, "PTP -> 2 scrcpy UP packets");
var assembler = new TouchpadFrameAssembler();
static TouchpadHidSample Hid(int id,int x,int y,bool on=true) => new(id,x,y,on);
var firstHybrid = assembler.Accept(2, 1000, new[]{Hid(11,100,200)});
Assert(firstHybrid is null && assembler.AwaitingContinuation, "first hybrid fragment does not release");
var secondHybrid = assembler.Accept(0, 1000, new[]{Hid(12,300,400)});
Assert(secondHybrid is {Length:2} && secondHybrid[0].ContactId==11 &&
       secondHybrid[1].ContactId==12 && !assembler.AwaitingContinuation,
       "0 contact-count continuation completes hybrid frame");
var tipLiftPart = assembler.Accept(2, 1010, new[]{Hid(11,120,195)});
Assert(tipLiftPart is null, "tip release hybrid is incomplete until continuation");
var tipLift = assembler.Accept(0, 1010, new[]{Hid(12,300,400,false)});
Assert(tipLift is {Length:1} && tipLift[0].ContactId==11,
       "tip switch off consumes HID contact slot without ghost touch");
var noFinger = assembler.Accept(0, 1020, Array.Empty<TouchpadHidSample>());
Assert(noFinger is {Length:0}, "explicit idle frame releases all");
Assert(assembler.Accept(0, 1030, new[]{Hid(13,500,600)}) is null,
       "orphaned active contact rejected");
Assert(assembler.Accept(2, 1040, new[]{Hid(21,10,10)}) is null,
       "missing hybrid continuation remains pending");
Assert(assembler.Accept(0, 1050, new[]{Hid(22,20,20)}) is null &&
       !assembler.AwaitingContinuation, "scan-time mismatch aborts partial frame");
Assert(assembler.Accept(2, 1060, new[]{Hid(31,10,20)}) is null,
       "new frame after mismatch");
var recovery=assembler.Accept(0,1060,new[]{Hid(32,30,40)});
Assert(recovery is {Length:2}, "assembler recovers after missing fragment");
var parallel=assembler.Accept(2,1070,new[]{Hid(41,100,200),Hid(42,500,600)});
Assert(parallel is {Length:2}, "two-in-one-report parallel mode");
Assert(assembler.Accept(20,1080,new[]{Hid(55,1,1)}) is null &&
       !assembler.AwaitingContinuation,"reject impossible count without waiting");
Console.WriteLine("PTP_HYBRID_ASSEMBLER_PASS single/parallel/hybrid/tip/scan/recovery");

Assert(new TouchMapping().Mode == TouchMappingMode.FullScreen, "absolute mapping must be default");
var fullScreen = new TouchMapping
{
    Mode = TouchMappingMode.FullScreen,
    ScreenWidth = 1920,
    ScreenHeight = 1080,
};
var fullMin = fullScreen.Map(new TouchpadContact(1, 0, 0), 0, 4010, 0, 2468);
var fullMax = fullScreen.Map(new TouchpadContact(1, 4010, 2468), 0, 4010, 0, 2468);
Assert(fullMin == (0, 0), "full-screen upper-left corner reachable with light contact");
Assert(fullMax == (1919, 1079), "full-screen lower-right corner reachable");
var fine = new TouchMapping { Mode=TouchMappingMode.GameLanes, ScreenWidth=1920, ScreenHeight=1080, VerticalSpan=.25 };
var normal = new TouchMapping { Mode=TouchMappingMode.GameLanes, ScreenWidth=1920, ScreenHeight=1080, VerticalSpan=.34 };
var fast = new TouchMapping { Mode=TouchMappingMode.GameLanes, ScreenWidth=1920, ScreenHeight=1080, VerticalSpan=.43 };
static int SwipePixels(TouchMapping m) =>
    Math.Abs(m.Map(new TouchpadContact(1,2000,1600),0,4010,0,2468).Y -
             m.Map(new TouchpadContact(1,2000,1250),0,4010,0,2468).Y);
Assert(SwipePixels(fast) > SwipePixels(normal) && SwipePixels(normal) > SwipePixels(fine),
    "flick amplification order");
Console.WriteLine("PTP_TOUCH_MODES_PASS full-screen corners / 12 lanes / flick presets");

Console.WriteLine("PTP_MAPPING_PASS 2 contacts / upward flick / release");
var five = new ScrcpyTouchPackets();
var fiveDown = Frame((1,100,500,80),(2,300,500,80),(3,500,500,80),
                     (4,700,500,80),(5,900,500,80));
Assert(five.Translate(fiveDown,1920,1080).Count==5 &&
       five.ActiveCount==5, "5-finger chord supported by codec");
var replacement = Frame((1,100,500,80),(2,300,500,0),(3,500,500,80),
                        (4,700,500,80),(5,900,500,80),(6,1100,500,80));
var changes = five.Translate(replacement,1920,1080);
Assert(changes.Count==2 && changes[0][1]==1 && changes[1][1]==0 &&
       five.ActiveCount==5, "contact churn sends UP before new DOWN");
Assert(five.ReleaseAll(1920,1080).Count==5 && five.ActiveCount==0,
       "remaining 5 fingers released");
Console.WriteLine("FIVE_FINGER_CODEC_PASS chord / churn / releases");
Console.WriteLine("PACKET_CODEC_PASS 2 DOWN / 2 MOVE / 2 UP; framing and release");
StressTests.Run();
await FramePumpTests.RunAsync();

if (args.Contains("--live"))
{
    Console.WriteLine("Connecting MuMu Android15, target 127.0.0.1:5555 (Home screen only).");
    await using ITouchBridge bridge = new ScrcpyTouchBridge();
    bridge.Faulted += err => Console.WriteLine("BRIDGE_FAULT " + err);
    await bridge.ConnectAsync("127.0.0.1:5555");
    Assert(bridge.Connected, "bridge connected");
    Console.WriteLine("BRIDGE_CONNECTED");
    Assert(bridge.Send(down), "two-finger down queued");
    await Task.Delay(300);
    Assert(bridge.Send(moved), "two-finger move queued");
    await Task.Delay(300);
    Assert(bridge.Send(release), "two-finger up queued");
    await Task.Delay(300);
    Console.WriteLine("BRIDGE_TWO_FINGERS_SENT");
}

// Explicit on-demand diagnostic, never used by the normal bridge.
if (args.Contains("--title-tap"))
{
    Console.WriteLine("TITLE_TAP_PROBE MuMu JP PJSK title only; one touch, no login");
    await using ITouchBridge bridge = new ScrcpyTouchBridge();
    bridge.Faulted += ex => Console.WriteLine("TITLE_BRIDGE_FAULT " + ex);
    await bridge.ConnectAsync("127.0.0.1:5555");
    Assert(bridge.Connected, "MuMu connected");
    var tapDown = Frame((901, 960, 922, 80));
    var tapUp = Frame((901, 960, 922, 0));
    Assert(bridge.Send(tapDown), "title down sent");
    await Task.Delay(160);
    Assert(bridge.Send(tapUp), "title up sent");
    await Task.Delay(750);
    Console.WriteLine("TITLE_TAP_SENT scrcpy reverse");
}
