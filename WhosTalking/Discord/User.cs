namespace WhosTalking.Discord;

public class User {
    public readonly string UserId;
    public bool? Deafened;
    public string? Discriminator;
    public string? DisplayName;
    public bool? Muted;
    public bool? Speaking;
    public string? Username;

    public User(
        string userId,
        string? username = null,
        string? discriminator = null,
        string? displayName = null,
        bool? muted = null,
        bool? deafened = null,
        bool? speaking = null
    ) {
        this.UserId = userId;
        this.Username = username;
        this.Discriminator = discriminator;
        this.DisplayName = displayName;
        this.Muted = muted;
        this.Deafened = deafened;
        this.Speaking = speaking;
    }

    public void Update(User other) {
        this.Username = other.Username ?? this.Username;
        this.Discriminator = other.Discriminator ?? this.Discriminator;
        this.DisplayName = other.DisplayName ?? this.DisplayName;
        this.Muted = other.Muted ?? this.Muted;
        this.Deafened = other.Deafened ?? this.Deafened;
        this.Speaking = other.Speaking ?? this.Speaking;
    }

    public UserState State {
        get {
            if (Deafened == true) {
                return UserState.Deafened;
            }

            if (Speaking == true) {
                return UserState.Speaking;
            }

            if (Muted == true) {
                return UserState.Muted;
            }

            return UserState.None;
        }
    }
}

public enum UserState: int {
    None = 0,
    Speaking = 1,
    Muted = 2,
    Deafened = 3
}
