using UnityEngine;
using UnityEngine.UI;

public class GameCursor : MonoBehaviour
{
    [SerializeField] private Image _cursor;
    [SerializeField] private Sprite _defaultCursor;
    [SerializeField] private Sprite _canInteractCursor;
    [SerializeField] private Sprite _isInteractingCursor;
    
    private static GameCursor Instance;

    private void Awake()
    {
        Instance = this;
    }

    public static void SetCursorDefault()
    {
        Instance._cursor.sprite = Instance._defaultCursor;
    }

    public static void SetCursorCanInteract()
    {
        Instance._cursor.sprite = Instance._canInteractCursor;
    }

    public static void SetCursorIsInteracting()
    {
        Instance._cursor.sprite = Instance._isInteractingCursor;
    }
}
