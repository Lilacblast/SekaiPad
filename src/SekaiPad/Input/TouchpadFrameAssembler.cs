using System;
using System.Collections.Generic;
using System.Linq;

namespace RawInput.Touchpad;

/// <summary>
/// A physical contact record in a HID input report. Even an inactive TIP
/// record counts toward Contact Count when reconstructing a hybrid frame.
/// </summary>
public readonly record struct TouchpadHidSample(
    int ContactId, int X, int Y, bool TipDown);

/// <summary>
/// Assemble Windows Precision Touchpad parallel, 1-finger hybrid and 2-finger
/// hybrid reports into full frames. For hybrid modes, the first report has a
/// nonzero 0x0d/0x54 Contact Count, while continuation reports use zero and
/// the same 0x0d/0x56 Scan Time.
///
/// A null return means no COMPLETE frame yet; an empty frame means all
/// fingers lifted. Never forward incomplete frames as an implicit UP.
/// </summary>
public sealed class TouchpadFrameAssembler
{
    private const int MaximumContacts = 10;
    private readonly List<TouchpadHidSample> _pending = new(MaximumContacts);
    private int _remaining;
    private uint _scanTime;

    public bool AwaitingContinuation => _remaining > 0;

    public TouchpadContact[]? Accept(uint contactCount, uint scanTime,
                                    IReadOnlyList<TouchpadHidSample> samples)
    {
        if (contactCount > MaximumContacts)
        {
            Reset();
            return null;
        }

        if (contactCount > 0)
        {
            // A fresh frame always supersedes an incomplete preceding frame.
            _pending.Clear();
            _remaining = (int)contactCount;
            _scanTime = scanTime;
        }
        else if (_remaining == 0)
        {
            // A no-contact/button-only packet is an explicit release.
            // An orphaned active contact with count=0 cannot be assigned to
            // a frame; discard it rather than send a phantom finger.
            return samples.Any(s => s.TipDown) ? null : Array.Empty<TouchpadContact>();
        }
        else if (scanTime != _scanTime)
        {
            // Missing or mismatched continuation. Never complete an uncertain
            // frame; next start packet will recover. WPF watchdog releases
            // any previously-held positions if complete frames stop.
            Reset();
            return null;
        }

        foreach (var sample in samples)
        {
            if (_remaining == 0) break;
            _pending.Add(sample);
            _remaining--;
        }

        if (_remaining > 0) return null;

        // The device may include TIP=0 records for a lifted finger; these
        // consume a contact slot but must not be forwarded as held fingers.
        var complete = _pending
            .Where(s => s.TipDown && s.ContactId >= 0)
            .GroupBy(s => s.ContactId)
            .Select(g => g.Last())
            .Select(s => new TouchpadContact(s.ContactId, s.X, s.Y))
            .ToArray();
        Reset();
        return complete;
    }

    public void Reset()
    {
        _pending.Clear();
        _remaining = 0;
        _scanTime = 0;
    }
}
