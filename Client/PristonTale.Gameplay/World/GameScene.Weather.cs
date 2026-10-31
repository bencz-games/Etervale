using PristonTale.Client.Net.Presence;
using PristonTale.Contracts.World;
using PristonTale.Engine3D.Rendering.Weather;
using PristonTale.Gameplay.Weather;
using PristonTale.Simulation.World;

namespace PristonTale.Gameplay.World;

// The weather of the local player's field: the server's (clear until it sends one) eased in by WeatherPresentation,
// unless the Weather setting is off or the field shown takes no rain; the renderer's sky, light and fog, the rain and
// its splashes (PristonRain, drawn after the opaque scene), the rain's sound, softer under a roof, and the thunder.
public sealed partial class GameScene
{
    // A roof this far above the player shelters it from the rain's sound.
    private const float ShelterClearance = 40f;

    private readonly WeatherPresentation _weather = new();
    private PristonRain _rain;
    private bool _weatherSeen;

    public WeatherState ServerWeather { get; private set; }

    private bool WeatherShown => (_presentation?.Requested.WeatherEnabled ?? true) && (_currentField is null || WeatherSchedule.Rains(_currentField));

    private void ObserveWeather(PresenceUpdate update)
    {
        if (update.Weather is not { } weather)
        {
            return;
        }

        ServerWeather = weather;
        if (!_weatherSeen)
        {
            _weatherSeen = true;
            _weather.Snap(weather, WeatherShown);
        }
    }

    private void CreateRain()
    {
        _rain = new PristonRain(_graphicsDevice, _content, _log);
        _weather.Thunder += volume => _fieldAudio?.PlayThunder(volume);
    }

    // The field shown changed: a field without rain clears at once instead of easing out.
    private void SnapWeatherToField()
    {
        if (!WeatherShown)
        {
            _weather.Snap(WeatherState.Clear, enabled: false);
        }
    }

    // The loaded fields changed: the surfaces the rain stops on are read again.
    private void InvalidateGround()
    {
        _groundLoot?.InvalidateFloor();
        _rain?.Occlusion.Reset(_fieldCollision);
    }

    private void UpdateWeather(float seconds)
    {
        _weather.Update(ServerWeather, WeatherShown, seconds);
        if (_renderer is not null)
        {
            _renderer.Weather = _weather.View;
        }

        if (_rain is not null && _camera is not null && _playerBody is not null)
        {
            _rain.Update(_camera.Position, _playerBody.Position, seconds, _weather.Rain);
        }

        var sheltered = _rain is not null && _playerBody is not null && _rain.Occlusion.Sheltered(_playerBody.Position, ShelterClearance);
        _fieldAudio?.UpdateWeather(_weather.Cloud, _weather.Rain, sheltered, seconds);
    }

    private void DrawRain()
    {
        if (_rain is not null && _camera is not null)
        {
            _rain.Draw(_camera.View, _camera.Projection, _camera.Position, _weather.View, _renderer.RainRadiance);
        }
    }
}
