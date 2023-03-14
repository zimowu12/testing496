using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Numerics;
using Cinemachine;
using Unity.Mathematics;
using UnityEngine;
using Unity.Netcode;
using UnityEngine.PlayerLoop;
using Quaternion = UnityEngine.Quaternion;
using Random = UnityEngine.Random;
using Vector2 = UnityEngine.Vector2;
using Vector3 = UnityEngine.Vector3;

public class Movement : NetworkBehaviour
{
	public float ChallengerTime;
	public float ControllerTime;
	private NetworkVariable<float> syncSpeed = new NetworkVariable<float>();
	private NetworkVariable<float> syncDir = new NetworkVariable<float>();
	private NetworkVariable<bool> syncIsSp = new NetworkVariable<bool>();
	private NetworkVariable<Vector3> syncVec = new NetworkVariable<Vector3>();
	private NetworkVariable<Quaternion> syncRot = new NetworkVariable<Quaternion>();

	public bool useCharacterForward = false;
	public float turnSpeed = 10f;
	
	private bool isC;
	private bool hostCanMove = false;
	private bool begin = false;
	
	private float turnSpeedMultiplier;
	private float speed = 0f;
	private float direction = 0f;
	private bool isSprinting = false;
	private Animator anim;
	private Vector3 targetDirection;
	private Vector2 input;
	private Quaternion freeRotation;
	private Camera mainCamera;
	private float velocity;
	private bool slow = false;
	private bool chaos = false;
	private bool blind = false;
	private bool stun = false;
	private bool growUp = false;
	private bool invisible = false;
	private Vector3 lastPos = new Vector3();
	private bool obstacleDestroy;
	private bool obstacleCanUse;
	private bool trapCanUse;
	
	private float timer = 0;
	private float stunTimer = 0;
	public float delayTime = 10f;
	private float stunSpeed = 1f;
	private float chaosSpeed = 1f;
	private float slowSpeed = 1f;
	private float sprintingSpeed;
	public int turnCount = 15;
	private int obstacleActivateNum = 0;
	private string obstacle;

	// Use this for initialization
	void Start ()
	{
		anim = GetComponent<Animator>();
		mainCamera = transform.parent.Find("Camera").GetComponent<Camera>();
	}

	public bool GetHostCanMove
	{
		get { return hostCanMove; }
	}
	
	public void SetIsCTrue() { isC = true; }
	public void SetIsCFalse() { isC = false; }
	
	// Update is called once per frame
	private void Update()
	{
		if (IsLocalPlayer)
		{
			LocalInput();
		}
		if (!IsLocalPlayer)
		{
			SyncInput();
		}
	}

	[ServerRpc]
	void UpdateInputServerRpc(float speed, float dir, bool isSp, Vector3 vec, Quaternion rot)
	{
		syncDir.Value = dir;
		syncSpeed.Value = speed;
		syncIsSp.Value = isSp;
		syncVec.Value = vec;
		syncRot.Value = rot;
	}

	/// <summary>
	/// Game end
	/// </summary>
	[ServerRpc]
	void UpdateGameStatusServerRpc() { GameStatusClientRpc(); }

	[ClientRpc]
	void GameStatusClientRpc()
	{
		hostCanMove = false;
		GameObject.Find("Host").transform.Find("Player").transform.GetComponent<Movement>().begin = false;
		GameObject.Find("Client").transform.Find("Player").transform.GetComponent<Movement>().begin = false;
		GameObject.Find("Canvas").transform.Find("Timer").gameObject.SetActive(false);
		Debug.Log("Game over");
	}
	/// <summary>
	///  Game Start
	/// </summary>
	[ServerRpc]
	void UpdateBeginServerRpc() { BeginClientRpc(); }

	[ClientRpc]
	void BeginClientRpc()
	{
		begin = true;
		GameObject.Find("Canvas").transform.Find("Wait").gameObject.SetActive(false);
		GameObject.Find("Canvas").transform.Find("Timer").gameObject.SetActive(true);
		GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().begin = true;
		Debug.Log("Game Start");
		GameObject maze = GameManager.instance.CreateMaze();
		transform.position = maze.transform.Find("StartPos").transform.position;
		GameObject.Find("Client").transform.position = maze.transform.Find("Overview").transform.position;
		if (IsHost)
		{
			GameManager.instance.server = true;
			GameObject.Find("Canvas").transform.Find("ServerCards").gameObject.SetActive(true);
		}
		else
		{
			GameManager.instance.server = false;
			GameObject.Find("Canvas").transform.Find("ClientCards").gameObject.SetActive(true);
		}
	}
	
	/// <summary>
	/// Change Turn
	/// </summary>
	[ServerRpc]
	void UpdateTurnServerRpc() { TurnChangeClientRpc(); }

	[ClientRpc]
	void TurnChangeClientRpc()
	{
		if(hostCanMove) hostCanMove = false;
		else if(!hostCanMove) hostCanMove = true;
		bool canMove = GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().hostCanMove;
		if (canMove) GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().hostCanMove = false;
		else if (!canMove) GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().hostCanMove = true;
		if (!hostCanMove)
		{ 
			if (isSprinting) isSprinting = false;
			if (slow) slow = false;
			if (chaos) chaos = false;
			if (growUp) growUp = false;
			if (blind) blind = false;
			if (obstacleActivateNum == 1) obstacleDestroy = true;
			delayTime = ControllerTime;
			GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().delayTime = ControllerTime;
			GameManager.instance.SetClientCanUse(true);
			GameManager.instance.SetServerCanUse(false);
		}
		else if (hostCanMove)
		{
			lastPos = GameObject.Find("Host").transform.Find("Player").transform.localPosition;
			GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().lastPos = GameObject.Find("Host").
				transform.Find("Player").transform.localPosition;
			if (invisible) invisible = false;
			delayTime = ChallengerTime;
			GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().delayTime = ChallengerTime;
			GameManager.instance.SetClientCanUse(false);
			GameManager.instance.SetServerCanUse(true);
		}
		GameManager.instance.Draw();
	}


	[ServerRpc]
	void UpdateDestroyObstacleServerRpc(string n)
	{
		DestroyObstacleClientRpc(n);
	}

	[ClientRpc]
	void DestroyObstacleClientRpc(string n)
	{
		GameManager.instance.DestroyObstacle(n);
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().obstacleActivateNum = 0;
		GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().obstacleActivateNum = 0;
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().obstacleDestroy = false;
		GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().obstacleDestroy = false;
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().obstacleCanUse = false;
		GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().obstacleCanUse = false;
	}
	
	/// <summary>
	/// Slow down
	/// </summary>
	[ServerRpc]
	void UpdateSlowDownServerRpc() { SlowDownClientRpc(); }

	[ClientRpc]
	void SlowDownClientRpc()
	{
		if(IsOwner) return;
		GameObject.Find("Host").transform.Find("Player").transform.GetComponent<Movement>()
			.slow = true;
	}

	/// <summary>
	/// Purify Slow down
	/// </summary>
	[ServerRpc]
	void UpdatePurifyServerRpc() { PurifyClientRpc(); }

	[ClientRpc]
	void PurifyClientRpc()
	{
		if (IsOwner)
		{ 
			List<string> l = new List<string>();
			if (stun) l.Add("stun");
			if (slow) l.Add("slow");
			if (blind) l.Add("blind");
			if (chaos) l.Add("chaos");
			if (l.Count == 0)
			{
				Debug.Log("No debuff found");
				return;
			}
			int i = Random.Range(0, l.Count);
			string debuff = l[i];
			if (debuff == "stun") stun = false;
			else if (debuff == "slow") slow = false;
			else if (debuff == "blind") blind = false;
			else if (debuff == "chaos")  chaos = false;
			Debug.Log("Purify " + debuff);
		}
	}

	/// <summary>
	/// Chaos
	/// </summary>
	[ServerRpc]
	void UpdateChaosStatusServerRpc() { ChaosStatusClientRpc(); }

	[ClientRpc]
	void ChaosStatusClientRpc()
	{
		if(IsOwner) return;
		GameObject.Find("Host").transform.Find("Player").transform.GetComponent<Movement>()
			.chaos = true;
	}
	
	/// <summary>
	/// Acceleration
	/// </summary>
	[ServerRpc]
	void UpdateIsSprintingServerRpc() { IsSprintingClientRpc(); }

	[ClientRpc]
	void IsSprintingClientRpc() { isSprinting = true; }

	/// <summary>
	/// Blind
	/// </summary>
	[ServerRpc]
	void UpdateBlindServerRpc() { BlindClientRpc(); }

	[ClientRpc]
	void BlindClientRpc()
	{
		blind = true;
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().blind = true;
	}

	/// <summary>
	/// Stun 
	/// </summary>
	[ServerRpc]
	void UpdateBeTrappedServerRpc(string str) { BeTrappedClientRpc(str); }

	[ClientRpc]
	void BeTrappedClientRpc(string str)
	{
		stun = true;
		speed = 0f;
		isSprinting = false;
		Destroy(GameObject.Find(str).gameObject);
	}

	/// <summary>
	/// Stun over
	/// </summary>
	[ServerRpc]
	void UpdateNotStunServerRpc() { NotStunClientRpc(); }

	[ClientRpc]
	void NotStunClientRpc() { stun = false; }

	/// <summary>
	/// Place trap
	/// </summary>
	/// <param name="point"></param>
	[ServerRpc]
	void UpdatePlaceTrapServerRpc(Vector3 point) { PlaceTrapClientRpc(point); }
	
	[ClientRpc]
	void PlaceTrapClientRpc(Vector3 point) { GameManager.instance.CreateTrap(point); }

	/// <summary>
	/// Create Marks
	/// </summary>
	[ServerRpc]
	void UpdateMarkServerRpc(Vector3 pos) { MarkClientRpc(pos); }

	[ClientRpc]
	void MarkClientRpc(Vector3 pos) { GameManager.instance.CreateMark(pos); }

	
	/// <summary>
	/// Erase all Challenger's mark
	/// </summary>
	[ServerRpc]
	void UpdateEraseMarkServerRpc() { EraseMarkClientRpc(); }

	[ClientRpc]
	void EraseMarkClientRpc() { GameManager.instance.EraseMarks(); }
	
	/// <summary>
	/// Activate obstacle
	/// </summary>
	/// <param name="n"></param>
	[ServerRpc]
	void UpdateObstacleServerRpc(string n) { ObstacleClientRpc(n); }

	[ClientRpc]
	void ObstacleClientRpc(string n)
	{
		obstacleActivateNum += 1;
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().obstacleActivateNum += 1;
		obstacle = GameManager.instance.CreateObstacle(n);
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().obstacle = obstacle;
	}

	/// <summary>
	/// Growing up
	/// </summary>
	[ServerRpc]
	void UpdateGrowUpServerRpc()
	{
		GrowUpClientRpc();
	}

	[ClientRpc]
	void GrowUpClientRpc()
	{
		growUp = true;
	}

	/// <summary>
	/// Invisible
	/// </summary>
	[ServerRpc]
	void UpdateInvisibleServerRpc() { InvisibleClientRpc(); }

	[ClientRpc]
	void InvisibleClientRpc() 
	{ 
		transform.Find("Body").gameObject.SetActive(false);
		invisible = true;
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().invisible = true;
	}
	
	/// <summary>
	/// Cancel Invisible
	/// </summary>
	[ServerRpc]
	void UpdateCancelInvisibleServerRpc(){ CancelInvisibleClientRpc(); }

	[ClientRpc]
	void CancelInvisibleClientRpc()
	{
		invisible = false;
		transform.Find("Body").gameObject.SetActive(true);
		GameObject.Find("Host").transform.Find("Player").GetComponent<Movement>().invisible = false;
	}
	
	
	/// <summary>
	/// Teleport
	/// </summary>
	/// <param name="pos"></param>
	[ServerRpc] 
	void UpdateTeleportServerRpc(Vector3 pos) { TeleportClientRpc(pos); }

	[ClientRpc]
	void TeleportClientRpc(Vector3 pos)
	{
		GameObject.Find("Host").transform.Find("Player").transform.localPosition = pos;
	}

	/// <summary>
	/// Increase time left this turn
	/// </summary>
	[ServerRpc]
	void UpdateTimeServerRpc()
	{
		TimeClientRpc();
	}

	[ClientRpc]
	void TimeClientRpc()
	{
		timer -= 5;
		GameObject.Find("Client").transform.Find("Player").GetComponent<Movement>().timer -= 5;
		GameObject.Find("Canvas").transform.Find("Timer").GetComponent<TimeCounter>().TimeIncreased();
	}

	void SyncInput()
	{
		anim.SetFloat("Speed", syncSpeed.Value);
		anim.SetFloat("Direction", syncDir.Value);
		anim.SetBool("isSprinting", syncIsSp.Value);
		transform.position = syncVec.Value;
		transform.rotation = syncRot.Value;
	}


	void LocalInput()
	{
		if (isC) // Challenger
		{
			input.x = Input.GetAxis("Horizontal");
			input.y = Input.GetAxis("Vertical");

			// set speed to both vertical and horizontal inputs
			if (useCharacterForward) speed = Mathf.Abs(input.x) + input.y;
			else 
				speed = Mathf.Abs(input.x) + Mathf.Abs(input.y);

			speed = Mathf.Clamp(speed, 0f, 1f);
			speed = Mathf.SmoothDamp(anim.GetFloat("Speed"), speed, ref velocity, 0.1f);
			anim.SetFloat("Speed", speed);

			if (input.y < 0f && useCharacterForward)
				direction = input.y;
			else
					direction = 0f;

			anim.SetFloat("Direction", direction);

			// set sprinting
			anim.SetBool("isSprinting", isSprinting);

			// Update target direction relative to the camera view (or not if the Keep Direction option is checked)
			UpdateTargetDirection();
			if (input != Vector2.zero && targetDirection.magnitude > 0.1f)
			{
				Vector3 lookDirection = targetDirection.normalized;
				freeRotation = Quaternion.LookRotation(lookDirection, transform.up);
				var diferenceRotation = freeRotation.eulerAngles.y - transform.eulerAngles.y;
				var eulerY = transform.eulerAngles.y;

				if (diferenceRotation < 0 || diferenceRotation > 0) eulerY = freeRotation.eulerAngles.y;
				var euler = new Vector3(0, eulerY, 0);

				transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(euler),
					turnSpeed * turnSpeedMultiplier * Time.deltaTime);
			}
			
			// Change turn
			if (begin)
			{
				timer += Time.deltaTime;
				if (timer > delayTime)
				{
					UpdateTurnServerRpc();
					timer = 0;
					turnCount -= 1;
					if (obstacleDestroy) UpdateDestroyObstacleServerRpc(obstacle);
				}

				// Obstructionist win condition
				if (turnCount == 0)
				{
					UpdateGameStatusServerRpc();
					Debug.Log("Controller Win");
				}
			}

			// Be trapped
			if (stun)
			{
				stunTimer += Time.deltaTime;
				if (stunTimer > 4f)
				{
					UpdateNotStunServerRpc();
					stunTimer = 0;
				}
				stunSpeed = 0f;
			}

			else if (!stun) stunSpeed = 1f;
			if (slow) slowSpeed = 0.3f;
			else if (!slow) slowSpeed = 1f;
			if (chaos) chaosSpeed = -1f;
			else if (!chaos) chaosSpeed = 1f;
			if (isSprinting) sprintingSpeed = 1.5f;
			else if (!isSprinting) sprintingSpeed = 1f;
			
			// Grow up
			if (growUp)
			{
				transform.parent.Find("PlayerCameraControl").GetComponent<CinemachineVirtualCamera>().m_Lens.FieldOfView = 80f;
				transform.parent.Find("PlayerCameraControl").GetComponentInChildren<CinemachineFramingTransposer>().m_TrackedObjectOffset.y = 6;
				transform.parent.Find("PlayerCameraControl").GetComponent<CinemachineVirtualCamera>()
					.GetCinemachineComponent<CinemachineFramingTransposer>().m_CameraDistance = 6;
				transform.parent.Find("PlayerCameraControl").GetComponent<CinemachineCollider>().enabled = false;
			}
			
			else
			{
				transform.parent.Find("PlayerCameraControl").GetComponent<CinemachineVirtualCamera>().m_Lens.FieldOfView = 40f;
				transform.parent.Find("PlayerCameraControl").GetComponentInChildren<CinemachineFramingTransposer>().m_TrackedObjectOffset.y = 1.6f;
				transform.parent.Find("PlayerCameraControl").GetComponent<CinemachineVirtualCamera>()
					.GetCinemachineComponent<CinemachineFramingTransposer>().m_CameraDistance = 2.22f;
				transform.parent.Find("PlayerCameraControl").GetComponent<CinemachineCollider>().enabled = true;
			}

			// Blind
			if (blind) GameObject.Find("Host").transform.Find("Camera").GetComponent<Blur>().enabled = true; 
			else GameObject.Find("Host").transform.Find("Camera").GetComponent<Blur>().enabled = false;
			
			if(!invisible) UpdateCancelInvisibleServerRpc();

			if (!hostCanMove) transform.position += transform.forward * 0f;

			else transform.position += transform.forward * speed * Time.deltaTime * 5 * slowSpeed * chaosSpeed * stunSpeed * sprintingSpeed;

			if (hostCanMove) // Abilites
			{

				// Mark
				if (Input.GetMouseButtonUp(0))
				{
					Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
					RaycastHit hit;
					if (Physics.Raycast(ray, out hit,Mathf.Infinity,1 << 0,QueryTriggerInteraction.Ignore) && hit.transform.name != "Player")
					{
						Vector3 point = hit.point;
						UpdateMarkServerRpc(point);
						// Debug.Log(hit.transform.name);
					}
				}
			}
		}

		if (!isC) // Controller
		{
			// Camera Movement
			UpdateDirection();
	
			// always false
			slow = false;
			stun = false;
			chaos = false;
			blind = false;
			invisible = false;

			if (!hostCanMove) // Abilities
			{
				// Trap
				if (Input.GetMouseButtonUp(0))
				{
					Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
					RaycastHit hit;
					if (Physics.Raycast(ray, out hit,Mathf.Infinity, 1<<3,QueryTriggerInteraction.Ignore))
					{
						// Debug.Log(hit.transform.name);
						Vector3 point = hit.point;
						UpdatePlaceTrapServerRpc(point);
						
					}
				}

				// Obstacle
				if (Input.GetMouseButtonUp(1) && obstacleActivateNum < 1 && obstacleCanUse)
				{
					Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
					RaycastHit hit;
					if (Physics.Raycast(ray, out hit)&& hit.transform.tag == "Obstacle")
					{
						string n = hit.transform.name;
						UpdateObstacleServerRpc(n);
					}
				}
			}
		}

		UpdateInputServerRpc(speed, direction, isSprinting, transform.position, transform.rotation);
	}
	
	/// <summary>
	/// Update camera direction to the player mouse position
	/// </summary>
	protected virtual void UpdateTargetDirection()
	{
		if (!useCharacterForward)
		{
			turnSpeedMultiplier = 1f;
			var forward = mainCamera.transform.TransformDirection(Vector3.forward);
			forward.y = 0;

			//get the right-facing direction of the referenceTransform
			var right = mainCamera.transform.TransformDirection(Vector3.right);

			// determine the direction the player will face based on input and the referenceTransform's right and forward directions
			targetDirection = input.x * right + input.y * forward;
		}
		else
		{
			turnSpeedMultiplier = 1f;
			var forward = transform.TransformDirection(Vector3.forward);
			forward.y = 0;

			//get the right-facing direction of the referenceTransform
			var right = transform.TransformDirection(Vector3.right);
			targetDirection = input.x * right + Mathf.Abs(input.y) * forward; 
		}
	}

	private void OnTriggerEnter(Collider other)
	{
		// Challenger win condition
		if (other.tag == "Finish")
		{
			UpdateGameStatusServerRpc();
			Debug.Log("Challenger win");
		}
	
		// Be trapped
		if (other.tag == "Trap")
		{
			UpdateBeTrappedServerRpc(other.name);
			Debug.Log("Stun");
		}
	}

	/// <summary>
	/// Controller perspective control
	/// </summary>
	void UpdateDirection()
	{
		

		mainCamera.transform.localRotation = Quaternion.Euler(90, 0, 0);
		if (Input.GetAxis("Mouse ScrollWheel") < 0)
		{
			if (mainCamera.fieldOfView <= 75)
				mainCamera.fieldOfView += 2;
			if (mainCamera.orthographicSize <= 15)
				mainCamera.orthographicSize += 0.5F;
		}

		//Zoom in
		if (Input.GetAxis("Mouse ScrollWheel") > 0)
		{
			if (mainCamera.fieldOfView > 2)
				mainCamera.fieldOfView -= 2;
			if (mainCamera.orthographicSize >= 1)
				mainCamera.orthographicSize -= 0.5F;
		}

		if (Input.GetKey(KeyCode.W))
		{
			transform.parent.Find("Camera").Translate(Vector3.up * Time.deltaTime * 2f);
		}

		if (Input.GetKey(KeyCode.S))
		{
			transform.parent.Find("Camera").Translate(Vector3.down * Time.deltaTime * 2f);
		}

		if (Input.GetKey(KeyCode.A))
		{
			transform.parent.Find("Camera").Translate(Vector3.left * Time.deltaTime * 2f);
		}

		if (Input.GetKey(KeyCode.D))
		{
			transform.parent.Find("Camera").Translate(Vector3.right * Time.deltaTime * 2f);
		}
	}

	public bool StarGame()
	{
		if (GameObject.Find("Client"))
		{
			UpdateBeginServerRpc();
			UpdateTurnServerRpc();
			return true;
		}
		else
		{
			//GameManager.instance.CreateMaze();
			//hostCanMove = true;
			Debug.Log("No Obstructionist enter the game, need one more player");
			return false;
		}
	}
	
	public void Purify()
	{
		if (hostCanMove) UpdatePurifyServerRpc();
	}

	public void Accelerate()
	{
		if (hostCanMove) UpdateIsSprintingServerRpc();
	}
	
	public void GrowUp()
	{
		if (hostCanMove) UpdateGrowUpServerRpc();
	}

	public void IncreaseTime()
	{
		if (hostCanMove) UpdateTimeServerRpc();
	}

	public void Invisible()
	{
		if (hostCanMove) UpdateInvisibleServerRpc();
	}
	
	public void Slow()
	{
		if (!hostCanMove) UpdateSlowDownServerRpc();
	}

	public void Chaos()
	{
		if (!hostCanMove) UpdateChaosStatusServerRpc();
	}
	
	public void Blind()
	{
		if (!hostCanMove) UpdateBlindServerRpc();
	}

	public void Teleport()
	{
		if (!hostCanMove) UpdateTeleportServerRpc(lastPos);
	}

	public void Obstacle()
	{
		if (!hostCanMove) obstacleCanUse = true;
	}

	public void Erase()
	{
		if (!hostCanMove) UpdateEraseMarkServerRpc();
	}

	public void Trap()
	{
		if (!hostCanMove) trapCanUse = true;
	}
}
