//! Win32 process control: Job Object containment, thread suspension for Pause/Resume, and
//! decoding of Robocopy's OEM code page output.

use std::collections::HashSet;
use std::os::windows::io::AsRawHandle;
use std::process::Child;

use windows::Win32::Foundation::{CloseHandle, ERROR_INVALID_PARAMETER, HANDLE};
use windows::Win32::Globalization::{CP_OEMCP, MULTI_BYTE_TO_WIDE_CHAR_FLAGS, MultiByteToWideChar};
use windows::Win32::System::Diagnostics::ToolHelp::{
    CreateToolhelp32Snapshot, TH32CS_SNAPTHREAD, THREADENTRY32, Thread32First, Thread32Next,
};
use windows::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION, JobObjectExtendedLimitInformation, SetInformationJobObject,
};
use windows::Win32::System::Threading::{OpenThread, ResumeThread, SuspendThread, THREAD_SUSPEND_RESUME};
use windows::core::PCWSTR;

/// A Job Object that kills Robocopy if RobustCopy exits without stopping it.
pub struct ProcessJob(HANDLE);

// The handle is an owned kernel object; Win32 job APIs are safe to call from any thread.
unsafe impl Send for ProcessJob {}

impl ProcessJob {
    pub fn new() -> Result<Self, String> {
        let handle = unsafe { CreateJobObjectW(None, PCWSTR::null()) }
            .map_err(|error| format!("Unable to create a Windows Job Object: {error}"))?;
        let job = Self(handle);

        let mut information = JOBOBJECT_EXTENDED_LIMIT_INFORMATION::default();
        information.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        unsafe {
            SetInformationJobObject(
                job.0,
                JobObjectExtendedLimitInformation,
                (&raw const information).cast(),
                size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            )
        }
        .map_err(|error| format!("Unable to configure the Windows Job Object: {error}"))?;
        Ok(job)
    }

    pub fn assign(&self, child: &Child) -> Result<(), String> {
        unsafe { AssignProcessToJobObject(self.0, HANDLE(child.as_raw_handle())) }
            .map_err(|error| format!("Unable to contain Robocopy in the Windows Job Object: {error}"))
    }
}

impl Drop for ProcessJob {
    fn drop(&mut self) {
        let _ = unsafe { CloseHandle(self.0) };
    }
}

struct ThreadHandle(HANDLE);

impl ThreadHandle {
    fn open(thread_id: u32) -> windows::core::Result<Self> {
        unsafe { OpenThread(THREAD_SUSPEND_RESUME, false, thread_id) }.map(Self)
    }
}

impl Drop for ThreadHandle {
    fn drop(&mut self) {
        let _ = unsafe { CloseHandle(self.0) };
    }
}

/// Suspends every thread of a process so that Pause keeps Robocopy in memory for Resume.
#[derive(Default)]
pub struct ProcessSuspender {
    suspended_threads: HashSet<u32>,
}

impl ProcessSuspender {
    pub fn suspend(&mut self, process_id: u32) -> Result<(), String> {
        if !self.suspended_threads.is_empty() {
            return Ok(());
        }

        let result = self.suspend_threads(process_id);
        if result.is_err() {
            self.resume_all();
        }

        result
    }

    fn suspend_threads(&mut self, process_id: u32) -> Result<(), String> {
        for thread_id in process_threads(process_id)? {
            let handle = match ThreadHandle::open(thread_id) {
                Ok(handle) => handle,
                // The thread exited after the snapshot.
                Err(error) if error.code() == ERROR_INVALID_PARAMETER.to_hresult() => continue,
                Err(error) => return Err(format!("Unable to open Robocopy thread {thread_id}: {error}")),
            };

            if unsafe { SuspendThread(handle.0) } == u32::MAX {
                return Err(format!(
                    "Unable to suspend Robocopy thread {thread_id}: {}",
                    windows::core::Error::from_thread()
                ));
            }

            self.suspended_threads.insert(thread_id);
        }

        if self.suspended_threads.is_empty() {
            return Err("Robocopy had no active threads to pause.".to_owned());
        }

        Ok(())
    }

    pub fn resume_all(&mut self) {
        for thread_id in self.suspended_threads.drain() {
            if let Ok(handle) = ThreadHandle::open(thread_id) {
                unsafe { ResumeThread(handle.0) };
            }
        }
    }
}

fn process_threads(process_id: u32) -> Result<Vec<u32>, String> {
    let snapshot = unsafe { CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0) }
        .map_err(|error| format!("Unable to list Robocopy threads: {error}"))?;
    let snapshot = ThreadHandle(snapshot);

    let mut threads = Vec::new();
    let mut entry = THREADENTRY32 { dwSize: size_of::<THREADENTRY32>() as u32, ..Default::default() };
    let mut more = unsafe { Thread32First(snapshot.0, &mut entry) }.is_ok();
    while more {
        if entry.th32OwnerProcessID == process_id {
            threads.push(entry.th32ThreadID);
        }

        more = unsafe { Thread32Next(snapshot.0, &mut entry) }.is_ok();
    }

    Ok(threads)
}

/// Decodes bytes in the system OEM code page. Robocopy writes redirected output in this code page,
/// which follows the system locale rather than the user's regional format.
pub fn decode_oem(bytes: &[u8]) -> String {
    if bytes.is_empty() {
        return String::new();
    }

    let flags = MULTI_BYTE_TO_WIDE_CHAR_FLAGS(0);
    let length = unsafe { MultiByteToWideChar(CP_OEMCP, flags, bytes, None) };
    if length <= 0 {
        return String::from_utf8_lossy(bytes).into_owned();
    }

    let mut wide = vec![0u16; length as usize];
    let written = unsafe { MultiByteToWideChar(CP_OEMCP, flags, bytes, Some(&mut wide)) };
    String::from_utf16_lossy(&wide[..written.max(0) as usize])
}
