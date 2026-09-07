using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUIController : MonoBehaviour
{
	[Header("Dependencies")]
	[SerializeField] private UGSManager ugsManager;

	[Header("UI Elements")]
	[SerializeField] private Button hostButton;
	[SerializeField] private Button joinButton;
	[SerializeField] private TMP_InputField codeInputField;
	[SerializeField] private TextMeshProUGUI statusText;

	private void Start()
	{
		// Bind UI button click events directly in code
		hostButton.onClick.AddListener(OnHostClicked);
		joinButton.onClick.AddListener(OnJoinClicked);

		UpdateStatus("Ready to connect.");
	}

	private void OnHostClicked()
	{
		SetButtonsInteractable(false);
		UpdateStatus("Creating lobby & initializing Relay...");

		// Call the host method on your manager
		ugsManager.CreateLobbyAndHost();
	}

	private void OnJoinClicked()
	{
		string inputCode = codeInputField.text.Trim().ToUpper();

		if (string.IsNullOrEmpty(inputCode))
		{
			UpdateStatus("Please enter a valid Join Code!");
			return;
		}

		SetButtonsInteractable(false);
		UpdateStatus($"Joining lobby {inputCode}...");

		// Call the join method on your manager
		ugsManager.JoinLobbyByCode(inputCode);
	}

	private void SetButtonsInteractable(bool state)
	{
		hostButton.interactable = state;
		joinButton.interactable = state;
	}

	public void UpdateStatus(string text)
	{
		if (statusText != null)
		{
			statusText.text = text;
		}
	}

	private void OnDestroy()
	{
		// Unbind listeners on destroy to prevent memory leaks
		hostButton.onClick.RemoveListener(OnHostClicked);
		joinButton.onClick.RemoveListener(OnJoinClicked);
	}
}