using Microsoft.JSInterop;

namespace Engine.UI.Services;

/// <summary>
/// Plays the short move sounds. Wrapped in a service so components never talk to
/// the audio layer directly, and so a browser that blocks audio degrades to silence
/// rather than to an exception in the middle of a move.
/// </summary>
public sealed class SoundPlayer(IJSRuntime js) {
    public bool Enabled { get; set; } = true;

    public async Task PlayAsync(string sound) {
        if (!Enabled) return;

        try {
            await js.InvokeVoidAsync("engineChess.play", sound);
        } catch (JSException) {
            // Audio is unavailable until the page has been interacted with, and on
            // some browsers not at all. Neither is worth surfacing.
        } catch (InvalidOperationException) {
            // Raised when a sound is requested during prerendering.
        }
    }

    public void Toggle() => Enabled = !Enabled;
}
